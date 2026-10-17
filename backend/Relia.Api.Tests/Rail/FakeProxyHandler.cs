using System.Net;
using System.Text;

namespace Relia.Api.Tests.Rail;

/// <summary>Rail Proxy falso: registra cada petición y responde lo que la prueba indique.</summary>
public class FakeProxyHandler : HttpMessageHandler
{
    public record Recorded(HttpMethod Method, string PathAndQuery, Dictionary<string, string> Headers, string? Body);

    public List<Recorded> Requests { get; } = [];
    private Func<HttpRequestMessage, HttpResponseMessage> _respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

    public void RespondWith(HttpStatusCode status, string json, string contentType = "application/json") =>
        _respond = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, contentType),
        };

    public void Throw(Exception exception) => _respond = _ => throw exception;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new Recorded(request.Method, request.RequestUri!.PathAndQuery, headers, body));
        return _respond(request);
    }
}
