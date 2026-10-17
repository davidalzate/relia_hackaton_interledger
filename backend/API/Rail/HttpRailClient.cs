using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Relia.Api.Rail;

/// <summary>Implementación HTTP de <see cref="IRailClient"/> contra el Rail Proxy.</summary>
public class HttpRailClient(HttpClient http, IOptions<RailProxyOptions> options) : IRailClient
{
    private const string Cop = "COP";
    private const int CopScale = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public async Task<RailWalletAddress?> ResolveWalletAddressAsync(string pointer, CancellationToken ct = default)
    {
        using var message = NewRequest(HttpMethod.Get, $"wallet-addresses?pointer={Uri.EscapeDataString(pointer)}");
        using var response = await SendAsync(message, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await ThrowIfErrorAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<RailWalletAddress>(Json, ct);
    }

    public async Task<RailPayment> CreatePaymentAsync(CreateRailPayment request, CancellationToken ct = default)
    {
        using var message = NewRequest(HttpMethod.Post, "payments");
        message.Headers.Add("Idempotency-Key", request.IdempotencyKey.ToString());
        message.Content = JsonContent.Create(new CreatePaymentBody(
            request.Direction, request.Counterparty, ToRail(request.ReceiveAmount), request.ExpiresAt,
            request.Description), options: Json);

        using var response = await SendAsync(message, ct);
        await ThrowIfErrorAsync(response, ct);
        return await ReadPaymentAsync(response, ct);
    }

    public async Task<RailPayment?> GetPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        using var message = NewRequest(HttpMethod.Get, $"payments/{paymentId}");
        using var response = await SendAsync(message, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await ThrowIfErrorAsync(response, ct);
        return await ReadPaymentAsync(response, ct);
    }

    public async Task<RailPayment> CancelPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        using var message = NewRequest(HttpMethod.Post, $"payments/{paymentId}/cancel");
        using var response = await SendAsync(message, ct);
        try
        {
            await ThrowIfErrorAsync(response, ct);
        }
        catch (RailRejectedException e) when (e.Code == "payment_not_cancellable")
        {
            throw new RailPaymentNotCancellableException(paymentId);
        }
        return await ReadPaymentAsync(response, ct);
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, path);
        message.Headers.Add("X-Api-Key", options.Value.ApiKey);
        return message;
    }

    /// <summary>Envía al proxy. Red caída o timeout → riel no disponible; la cancelación de quien llama se respeta.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(message, ct);
        }
        catch (HttpRequestException e)
        {
            throw new RailUnavailableException($"El proxy no respondió: {e.Message}", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new RailUnavailableException("El proxy no respondió a tiempo", e);
        }
    }

    private static async Task<RailPayment> ReadPaymentAsync(HttpResponseMessage response, CancellationToken ct) =>
        ToPayment((await response.Content.ReadFromJsonAsync<PaymentBody>(Json, ct))!);

    /// <summary>Traduce las respuestas de error del proxy (problem+json con code) a excepciones del cliente.</summary>
    private static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var status = (int)response.StatusCode;
        var problem = await ReadProblemAsync(response, ct);
        if (status == StatusCodes.Status503ServiceUnavailable)
            throw new RailUnavailableException($"{problem.Code}: {problem.Detail ?? problem.Title}");
        throw new RailRejectedException(status, problem.Code ?? "unknown", problem.Detail ?? problem.Title);
    }

    private static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemBody>(Json, ct) ?? new ProblemBody(null, null, null);
        }
        catch (JsonException)
        {
            return new ProblemBody(null, null, null);
        }
    }

    // ---- Montos: pesos COP ↔ unidades mínimas del riel ----

    private static AmountBody ToRail(long pesos) =>
        new((pesos * (long)Math.Pow(10, CopScale)).ToString(), Cop, CopScale);

    private static long ToPesos(AmountBody amount)
    {
        if (amount.AssetCode != Cop)
            throw new RailRejectedException(200, "unsupported_asset", $"El riel devolvió {amount.AssetCode}");
        return long.Parse(amount.Value) / (long)Math.Pow(10, amount.AssetScale);
    }

    private static RailPayment ToPayment(PaymentBody p) => new(
        p.Id, p.IdempotencyKey, p.Direction, p.Status, p.Sender, p.Receiver,
        ToPesos(p.ReceiveAmount),
        p.DebitAmount is null ? null : ToPesos(p.DebitAmount),
        p.ReceivedAmount is null ? null : ToPesos(p.ReceivedAmount),
        p.Approval?.Url, p.ExpiresAt, p.FailureCode, p.FailureDetail,
        p.Rail?.IncomingPaymentUrl, p.Rail?.QuoteUrl, p.Rail?.OutgoingPaymentUrl,
        p.CreatedAt, p.UpdatedAt, p.CompletedAt, p.Version);

    // ---- Cuerpos JSON del contrato del proxy ----

    private record AmountBody(string Value, string AssetCode, int AssetScale);

    private record CreatePaymentBody(
        RailDirection Direction, string Counterparty, AmountBody ReceiveAmount,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ExpiresAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description);

    private record ProblemBody(string? Code, string? Title, string? Detail);

    private record ApprovalBody(string Method, string? Url, string? Payload);

    private record RailReferencesBody(string? IncomingPaymentUrl, string? QuoteUrl, string? OutgoingPaymentUrl);

    private record PaymentBody(
        Guid Id, Guid IdempotencyKey, RailDirection Direction, RailPaymentStatus Status,
        string Sender, string Receiver, AmountBody ReceiveAmount, AmountBody? DebitAmount,
        AmountBody? ReceivedAmount, ApprovalBody? Approval, DateTimeOffset? ExpiresAt, string? FailureCode,
        string? FailureDetail, RailReferencesBody? Rail, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
        DateTimeOffset? CompletedAt, int Version);
}
