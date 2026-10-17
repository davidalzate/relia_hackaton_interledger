using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Relia.Api.Tests.Support;

public static class Http
{
    public static async Task<JsonElement> Json(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Esperaba {(int)expected}, llegó {(int)response.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Verifica un problem+json con el code del contrato.</summary>
    public static async Task<JsonElement> Problem(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var json = await response.Json(status);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, json.GetProperty("code").GetString());
        return json;
    }

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object body,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PostEmpty(this HttpClient client, string url) =>
        client.PostAsync(url, null);

    public static string Key() => $"key-{Guid.NewGuid():N}";

    public static long Long(this JsonElement json, string path) => json.At(path).GetInt64();

    public static string? Str(this JsonElement json, string path) => json.At(path).GetString();

    /// <summary>Navega "balances.quota.after".</summary>
    public static JsonElement At(this JsonElement json, string path)
    {
        foreach (var part in path.Split('.')) json = json.GetProperty(part);
        return json;
    }
}
