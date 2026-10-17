using Relia.Api.Errors;

namespace Relia.Api.Rail;

// Cliente del Rail Proxy (contracts/rail-proxy.openapi.yaml). Es la única puerta del backend hacia el riel:
// el backend no conoce Open Payments (ADR 0001). Montos en pesos COP; la conversión a
// { value, assetCode, assetScale } es interna del cliente.

public enum RailDirection { Outgoing, Incoming }

public enum RailPaymentStatus { PendingApproval, Processing, Completed, Failed, Rejected, Expired, Cancelled }

public record RailWalletAddress(string Pointer, string Url, string AssetCode, int AssetScale);

/// <param name="IdempotencyKey">Id del pago en el backend (payments.id).</param>
/// <param name="ExpiresAt">Obligatorio en incoming: plazo para aprobar.</param>
public record CreateRailPayment(
    Guid IdempotencyKey, RailDirection Direction, string Counterparty, long ReceiveAmount,
    DateTimeOffset? ExpiresAt = null, string? Description = null);

/// <summary>Pago de riel. Montos en pesos COP; null mientras el riel no los informe.</summary>
public record RailPayment(
    Guid Id, Guid IdempotencyKey, RailDirection Direction, RailPaymentStatus Status,
    string Sender, string Receiver, long ReceiveAmount, long? DebitAmount, long? ReceivedAmount,
    string? ApprovalUrl, DateTimeOffset? ExpiresAt, string? FailureCode, string? FailureDetail,
    string? IncomingPaymentUrl, string? QuoteUrl, string? OutgoingPaymentUrl,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CompletedAt, int Version);

public interface IRailClient
{
    /// <summary>Resuelve un payment pointer. Null si no existe en el riel.</summary>
    Task<RailWalletAddress?> ResolveWalletAddressAsync(string pointer, CancellationToken ct = default);

    /// <summary>Crea (o devuelve, por idempotencia) un pago de riel.</summary>
    Task<RailPayment> CreatePaymentAsync(CreateRailPayment request, CancellationToken ct = default);

    /// <summary>Último estado conocido por el proxy. Null si no existe.</summary>
    Task<RailPayment?> GetPaymentAsync(Guid paymentId, CancellationToken ct = default);

    /// <summary>Cancela un cobro incoming pendiente de aprobación.</summary>
    /// <exception cref="RailPaymentNotCancellableException">Ya se aprobó.</exception>
    Task<RailPayment> CancelPaymentAsync(Guid paymentId, CancellationToken ct = default);
}

/// <summary>
/// El riel no respondió o Relia no tiene permiso activo para pagar. No se creó ni cambió nada.
/// Llega al frontend como 503 rail_unavailable.
/// </summary>
public class RailUnavailableException(string detail, Exception? inner = null)
    : ApiException(StatusCodes.Status503ServiceUnavailable, "rail_unavailable", "El riel no está disponible")
{
    public string Detail { get; } = detail;
    public Exception? Inner { get; } = inner;
}

/// <summary>El cobro ya se aprobó y sigue su curso (409 payment_not_cancellable del proxy).</summary>
public class RailPaymentNotCancellableException(Guid paymentId)
    : Exception($"El pago de riel {paymentId} ya no se puede cancelar");

/// <summary>
/// El proxy rechazó la petición por una regla suya (unsupported_asset, same_wallet, idempotency_conflict,
/// validation_error...). Es un error de programación o de datos: el servicio decide cómo responder.
/// </summary>
public class RailRejectedException(int status, string code, string? detail)
    : Exception($"El riel rechazó la petición: {status} {code} {detail}")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public class RailProxyOptions
{
    public const string Section = "RailProxy";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
}
