using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using YamlDotNet.RepresentationModel;

namespace Relia.Api.Tests.Support;

/// <summary>
/// Valida respuestas HTTP contra contracts/relia-api.openapi.yaml: el código debe estar declarado para esa ruta
/// y método, y el cuerpo debe cumplir su schema (OpenAPI 3.1 usa JSON Schema 2020-12).
/// </summary>
public class ContractValidator
{
    public static readonly ContractValidator Relia = new(FindContract("relia-api.openapi.yaml"));

    private readonly JsonObject _doc;
    private readonly JsonObject _defs;
    private readonly List<(Regex Path, string Template)> _paths;
    private readonly ConcurrentDictionary<string, JsonSchema> _schemas = new();
    private int _ids;

    private ContractValidator(string file)
    {
        var yaml = new YamlStream();
        yaml.Load(new StringReader(File.ReadAllText(file)));
        _doc = (JsonObject)ToJson(yaml.Documents[0].RootNode)!;
        _defs = (JsonObject)Rewrite(_doc["components"]!["schemas"]!.DeepClone());
        _paths = ((JsonObject)_doc["paths"]!).Select(p => p.Key)
            .Select(t => (new Regex("^" + Regex.Replace(Regex.Escape(t), @"\\\{[^}]+}", "[^/]+") + "$"), t))
            .ToList();
    }

    /// <summary>Lista de problemas; vacía si la respuesta cumple el contrato.</summary>
    public IReadOnlyList<string> Check(string method, string path, int status, string? contentType, string body)
    {
        var template = _paths.FirstOrDefault(p => p.Path.IsMatch(path)).Template;
        if (template is null) return [$"{method} {path}: la ruta no está en el contrato"];

        var operation = _doc["paths"]![template]![method.ToLowerInvariant()];
        if (operation is null) return [$"{method} {template}: el método no está en el contrato"];

        var response = operation["responses"]?[status.ToString(CultureInfo.InvariantCulture)];
        if (response is null) return [$"{method} {template}: el código {status} no está declarado"];
        response = Resolve(response);

        var content = response["content"] as JsonObject;
        if (content is null || content.Count == 0)
            return body.Length == 0 ? [] : [$"{method} {template} {status}: el contrato no declara cuerpo"];

        var mediaType = contentType?.Split(';')[0].Trim();
        if (mediaType is null || content[mediaType] is not { } media)
            return [$"{method} {template} {status}: content-type {mediaType} no declarado ({string.Join(", ", content.Select(c => c.Key))})"];

        var key = $"{method} {template} {status} {mediaType}";
        var schema = _schemas.GetOrAdd(key, _ => Build(media["schema"]!));
        var result = schema.Evaluate(JsonDocument.Parse(body).RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (result.IsValid) return [];

        return result.Details?
                   .Where(d => d.Errors is { Count: > 0 })
                   .SelectMany(d => d.Errors!.Select(e => $"{key}: {d.InstanceLocation} {e.Key}: {e.Value}"))
                   .Take(10).ToList()
               ?? [$"{key}: el cuerpo no cumple el schema"];
    }

    private JsonNode Resolve(JsonNode node)
    {
        while (node["$ref"]?.GetValue<string>() is { } reference)
            node = reference.TrimStart('#', '/').Split('/').Aggregate((JsonNode)_doc, (n, part) => n[part.Replace("~1", "/")]!);
        return node;
    }

    private JsonSchema Build(JsonNode schema)
    {
        var root = (JsonObject)Rewrite(schema.DeepClone());
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        root["$defs"] = _defs.DeepClone();
        return JsonSchema.FromText(root.ToJsonString(), BuildOptions.Default,
            new Uri($"https://contract.relia.test/schema-{Interlocked.Increment(ref _ids)}"));
    }

    /// <summary>#/components/schemas/X → #/$defs/X.</summary>
    private static JsonNode Rewrite(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (name == "$ref" && value?.GetValue<string>() is { } r && r.StartsWith("#/components/schemas/"))
                        obj["$ref"] = "#/$defs/" + r["#/components/schemas/".Length..];
                    else if (value is not null) Rewrite(value);
                }
                break;
            case JsonArray array:
                foreach (var item in array) if (item is not null) Rewrite(item);
                break;
        }
        return node;
    }

    /// <summary>YAML → JSON, con tipos: los escalares sin comillas pueden ser número, booleano o null.</summary>
    private static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(c =>
            KeyValuePair.Create(((YamlScalarNode)c.Key).Value!, ToJson(c.Value)))),
        YamlSequenceNode seq => new JsonArray(seq.Children.Select(ToJson).ToArray()),
        YamlScalarNode { Style: YamlDotNet.Core.ScalarStyle.Plain } s => Plain(s.Value!),
        YamlScalarNode s => JsonValue.Create(s.Value),
        _ => null,
    };

    private static JsonNode? Plain(string value) => value switch
    {
        "null" or "~" => null,
        "true" => JsonValue.Create(true),
        "false" => JsonValue.Create(false),
        _ when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => JsonValue.Create(l),
        _ when Regex.IsMatch(value, @"^-?\d+\.\d+$") => JsonValue.Create(double.Parse(value, CultureInfo.InvariantCulture)),
        _ => JsonValue.Create(value),
    };

    private static string FindContract(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "contracts", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"No se encontró contracts/{name}");
    }
}

/// <summary>Valida cada respuesta que reciben las pruebas contra el contrato.</summary>
public class ContractCheckingHandler(ContractValidator contract) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        await response.Content.LoadIntoBufferAsync(ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var problems = contract.Check(request.Method.Method, request.RequestUri!.AbsolutePath, (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString(), body);
        if (problems.Count > 0)
            Assert.Fail($"La respuesta no cumple el contrato:\n{string.Join("\n", problems)}\nCuerpo: {body}");
        return response;
    }
}
