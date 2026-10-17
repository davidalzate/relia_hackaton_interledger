using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Relia.Api.Rail;

// Única costura hacia afuera: el proxy de riel (contracts/rail-proxy.openapi.yaml, ADR 0001). El backend no
// conoce Open Payments; solo pide resolver pointers, crear, consultar y cancelar pagos.

/// <summary>Billetera resuelta por el proxy: existe en el riel, con su moneda y escala.</summary>
public record RailWallet(string Pointer, string Url, string AssetCode, int AssetScale);

/// <summary>Monto del riel en unidades mínimas: COP con escala 2 → $50.000 = "5000000".</summary>
public record RailAmount(string Value, string AssetCode, int AssetScale)
{
    public const string Cop = "COP";
    public const int CopScale = 2;

    public static RailAmount FromCop(long pesos) =>
        new((pesos * 100).ToString(CultureInfo.InvariantCulture), Cop, CopScale);

    /// <summary>De vuelta a pesos enteros. Otra moneda o fracciones de peso: el riel no cumplió lo pedido.</summary>
    public long ToCop()
    {
        if (AssetCode != Cop || !BigInteger.TryParse(Value, NumberStyles.None, CultureInfo.InvariantCulture, out var units))
            throw new RailException("unsupported_asset", $"Monto del riel no es COP: {Value} {AssetCode}");
        var pesos = BigInteger.DivRem(units, BigInteger.Pow(10, AssetScale), out var cents);
        if (cents != 0 || pesos > long.MaxValue)
            throw new RailException("unsupported_asset", $"Monto del riel no es un número entero de pesos: {Value}");
        return (long)pesos;
    }
}

/// <summary>Visto desde Relia: outgoing = Relia paga (depósito, liquidación); incoming = Relia cobra.</summary>
public static class RailDirection
{
    public const string Outgoing = "outgoing";
    public const string Incoming = "incoming";
}

/// <summary>Cuerpo de POST /payments. La otra punta es siempre la billetera de Relia.</summary>
public record RailPaymentRequest(
    string Direction, string Counterparty, RailAmount ReceiveAmount, DateTimeOffset? ExpiresAt = null,
    string? Description = null);

public record RailApproval(string Method, string? Url, string? Payload);

public record RailReferences(string? IncomingPaymentUrl, string? QuoteUrl, string? OutgoingPaymentUrl);

/// <summary>
/// Pago de riel según el proxy. <see cref="Status"/>: pending_approval, processing, completed, failed, rejected,
/// expired o cancelled. <see cref="Version"/> crece con cada cambio. <see cref="DebitAmount"/> va en la moneda del
/// que paga y <see cref="ReceivedAmount"/> en la del que recibe; <see cref="QuotedReceiveAmount"/> es lo que el quote
/// promete entregar al receptor en su moneda (null si aún no hay quote).
/// </summary>
public record RailPayment(
    Guid Id, string Direction, string Status, string Sender, string Receiver, RailAmount ReceiveAmount,
    RailAmount? DebitAmount, RailAmount? ReceivedAmount, RailApproval? Approval, DateTimeOffset? ExpiresAt,
    string? FailureCode, string? FailureDetail, RailReferences? Rail, DateTimeOffset? CompletedAt, int Version,
    RailAmount? QuotedReceiveAmount = null);

/// <summary>
/// Error del proxy con su <c>code</c> (unsupported_asset, same_wallet, idempotency_conflict…). unsupported_asset
/// también significa que el riel no convierte entre COP y la moneda de la contraparte.
/// </summary>
public class RailException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>
/// El riel no está disponible: rail_unavailable, relia_grant_unavailable, timeout, error de red o respuesta
/// inesperada. No se creó ni cambió nada; se puede reintentar. Los servicios lo responden como 503.
/// </summary>
public class RailUnavailableException(string message, Exception? inner = null)
    : RailException("rail_unavailable", message, inner);

/// <summary>wallet_not_found: el payment pointer no existe en el riel.</summary>
public class RailWalletNotFoundException(string message) : RailException("wallet_not_found", message);

public interface IRailClient
{
    /// <summary>GET /wallet-addresses. Lanza RailWalletNotFoundException o RailUnavailableException.</summary>
    Task<RailWallet> ResolveWalletAddressAsync(string pointer, CancellationToken ct = default);

    /// <summary>
    /// POST /payments con <paramref name="paymentId"/> (payments.id del backend) como Idempotency-Key: repetirlo
    /// devuelve el mismo pago. Sin permiso de Relia (relia_grant_unavailable) lanza RailUnavailableException.
    /// </summary>
    Task<RailPayment> CreatePaymentAsync(Guid paymentId, RailPaymentRequest request, CancellationToken ct = default);

    /// <summary>GET /payments/{railPaymentId}: último estado conocido por el proxy.</summary>
    Task<RailPayment> GetPaymentAsync(Guid railPaymentId, CancellationToken ct = default);

    /// <summary>
    /// POST /payments/{railPaymentId}/cancel: solo incoming en pending_approval. Después de la aprobación lanza
    /// RailException con code payment_not_cancellable. Cancelar uno ya cancelado devuelve el mismo pago.
    /// </summary>
    Task<RailPayment> CancelPaymentAsync(Guid railPaymentId, CancellationToken ct = default);
}

/// <summary>
/// Cliente HTTP tipado del proxy. La URL base, el header X-Api-Key y el timeout los pone la configuración del
/// HttpClient en Program.cs. Toda falla sale como <see cref="RailException"/>; nunca otra excepción.
/// </summary>
public class RailClient(HttpClient http, ILogger<RailClient> logger) : IRailClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<RailWallet> ResolveWalletAddressAsync(string pointer, CancellationToken ct = default) =>
        SendAsync<RailWallet>(
            new HttpRequestMessage(HttpMethod.Get, $"wallet-addresses?pointer={Uri.EscapeDataString(pointer)}"), ct);

    // 201 (creado) y 200 (ya existía con esta clave) se tratan igual.
    public Task<RailPayment> CreatePaymentAsync(Guid paymentId, RailPaymentRequest request,
        CancellationToken ct = default) =>
        SendAsync<RailPayment>(
            new HttpRequestMessage(HttpMethod.Post, "payments")
            {
                Content = JsonContent.Create(request, options: Json),
                Headers = { { "Idempotency-Key", paymentId.ToString() } },
            }, ct);

    public Task<RailPayment> GetPaymentAsync(Guid railPaymentId, CancellationToken ct = default) =>
        SendAsync<RailPayment>(new HttpRequestMessage(HttpMethod.Get, $"payments/{railPaymentId}"), ct);

    public Task<RailPayment> CancelPaymentAsync(Guid railPaymentId, CancellationToken ct = default) =>
        SendAsync<RailPayment>(new HttpRequestMessage(HttpMethod.Post, $"payments/{railPaymentId}/cancel"), ct);

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var _ = request;
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning(e, "Proxy de riel inalcanzable: {Method} {Uri}", request.Method, request.RequestUri);
            throw new RailUnavailableException("Proxy de riel inalcanzable", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Proxy de riel sin respuesta a tiempo: {Method} {Uri}", request.Method, request.RequestUri);
            throw new RailUnavailableException("Proxy de riel sin respuesta a tiempo", e);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                           ?? throw new JsonException("Cuerpo vacío");
                }
                catch (Exception e) when (e is JsonException or NotSupportedException)
                {
                    logger.LogError(e, "Respuesta inválida del proxy de riel: {Uri}", request.RequestUri);
                    throw new RailUnavailableException("Respuesta inválida del proxy de riel", e);
                }
            }

            var code = await ReadCodeAsync(response, ct);
            var status = response.StatusCode;
            if (status == HttpStatusCode.Unauthorized)
                logger.LogError("El proxy de riel rechazó X-Api-Key: revise RailProxy:ApiKey");
            else
                logger.LogWarning("Proxy de riel respondió {Status} {Code}: {Method} {Uri}",
                    (int)status, code, request.Method, request.RequestUri);

            throw code switch
            {
                "wallet_not_found" => new RailWalletNotFoundException("El payment pointer no existe en el riel"),
                "rail_unavailable" or "relia_grant_unavailable" => new RailUnavailableException(code),
                // Credenciales mal configuradas, límite del proxy o fallo interno: para el usuario, no disponible.
                _ when status is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests
                       || (int)status >= 500 || code is null =>
                    new RailUnavailableException($"Proxy de riel respondió {(int)status} {code}"),
                _ => new RailException(code, $"Proxy de riel respondió {(int)status} {code}"),
            };
        }
    }

    /// <summary>El <c>code</c> del problem+json del proxy, o null si el cuerpo no lo trae.</summary>
    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProxyProblem>(Json, ct);
            return problem?.Code;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private record ProxyProblem(string? Code);
}
