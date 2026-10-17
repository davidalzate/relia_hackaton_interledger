using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Rail;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Lo que comparten operaciones y liquidaciones al hablar con el riel: el id del pago, la traducción de errores al
/// crearlo y cómo se registra y se copia cada lectura del proxy en payments.
/// </summary>
public static class RailPayments
{
    private static readonly JsonSerializerOptions RailJson = new(JsonSerializerDefaults.Web);

    /// <summary>Tope de la descripción que ve el pagador en su billetera (rail-proxy.openapi.yaml).</summary>
    private const int MaxDescription = 140;

    /// <summary>
    /// Descripción que ve el pagador, recortada al tope del proxy. Los llamadores no ponen el OP-xxxxxx ni el
    /// LQ-xxxxxx: el número cambia en cada reintento y el proxy vería otro cuerpo con la misma clave.
    /// </summary>
    public static string Description(string text) => text.Length > MaxDescription ? text[..MaxDescription] : text;

    /// <summary>
    /// payments.id, que también es la Idempotency-Key del riel, derivado de (corresponsal, tipo, clave del frontend).
    /// Si el riel creó el pago pero la respuesta no llegó (timeout → 503 y rollback), el reintento con la misma
    /// clave le pide al riel el mismo pago en vez de crear otro: no se paga dos veces.
    /// </summary>
    public static Guid PaymentId(Guid agentId, string kind, string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{agentId:N}:{kind}:{idempotencyKey}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// POST /payments al proxy. Riel caído o sin permiso → 503 rail_unavailable; billetera que el riel no acepta →
    /// 422 wallet_not_found; billetera en una moneda que el riel no convierte → 422 wallet_currency_unsupported. En
    /// todos los casos el filtro de idempotencia deshace lo creado.
    /// </summary>
    public static async Task<RailPayment> CreateOrThrowAsync(this IRailClient rail, Payment payment,
        RailPaymentRequest request, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await rail.CreatePaymentAsync(payment.Id, request, ct);
        }
        catch (RailUnavailableException e)
        {
            logger.LogWarning(e, "El riel no aceptó el pago {PaymentId}", payment.Id);
            throw ApiErrors.RailUnavailable();
        }
        catch (RailException e) when (e.Code == "unsupported_asset")
        {
            logger.LogWarning(e, "El riel no convierte a la moneda de la billetera {Wallet}", request.Counterparty);
            throw ApiErrors.WalletCurrencyUnsupported();
        }
        catch (RailException e) when (e.Code is "wallet_not_found" or "same_wallet")
        {
            logger.LogWarning(e, "El riel rechazó la billetera {Wallet} ({Code})", request.Counterparty, e.Code);
            throw ApiErrors.WalletNotFound();
        }
        catch (RailException e) when (e.Code == "idempotency_conflict")
        {
            // Se reintentó la misma Idempotency-Key con otro cuerpo después de un 503.
            throw new ApiException(StatusCodes.Status409Conflict, "idempotency_conflict",
                "La clave de idempotencia ya se usó con otra petición");
        }
        catch (RailException e)
        {
            logger.LogError(e, "Respuesta inesperada del riel al crear el pago {PaymentId} ({Code})", payment.Id, e.Code);
            throw ApiErrors.RailUnavailable();
        }
    }

    /// <summary>
    /// Toda respuesta del riel queda en la bitácora del pago, también las lecturas viejas que se descartan.
    /// Devuelve true si la lectura es vieja (version ≤ rail_version).
    /// </summary>
    public static bool LogReading(this IPaymentRepository payments, Payment payment, RailPayment reading,
        string action, OperationState state, DateTimeOffset now)
    {
        var stale = payment.RailVersion is { } processed && reading.Version <= processed;
        payments.AddEvent(new PaymentEvent
        {
            PaymentId = payment.Id,
            Action = action,
            PreviousState = payment.State,
            NewState = stale ? payment.State : state,
            Origin = EventOrigin.Rail,
            RailResponse = JsonSerializer.SerializeToDocument(reading, RailJson),
            Detail = $"{reading.Status} v{reading.Version}{(stale ? " (descartada)" : "")}",
            CreatedAt = now,
        });
        return stale;
    }

    public static void CopyRailData(this Payment payment, RailPayment reading)
    {
        payment.RailPaymentId = reading.Id;
        payment.RailVersion = reading.Version;
        payment.IncomingPaymentUrl = reading.Rail?.IncomingPaymentUrl ?? payment.IncomingPaymentUrl;
        payment.QuoteUrl = reading.Rail?.QuoteUrl ?? payment.QuoteUrl;
        payment.OutgoingPaymentUrl = reading.Rail?.OutgoingPaymentUrl ?? payment.OutgoingPaymentUrl;
        // El proxy solo trae approval mientras espera la aprobación: después ya no se ofrece aprobar (la URL vieja
        // queda en payment_events).
        payment.ApprovalUrl = reading.Status == "pending_approval" ? reading.Approval?.Url ?? payment.ApprovalUrl : null;
        payment.ExpiresAt = reading.ExpiresAt ?? payment.ExpiresAt;
        if (reading.DebitAmount is { } debit)
            (payment.DebitValue, payment.DebitAssetCode, payment.DebitAssetScale) =
                (debit.Value, debit.AssetCode, (short)debit.AssetScale);
        if (reading.ReceivedAmount is { } received)
            (payment.ReceivedValue, payment.ReceivedAssetCode, payment.ReceivedAssetScale) =
                (received.Value, received.AssetCode, (short)received.AssetScale);
        if (reading.QuotedReceiveAmount is { } quoted)
            (payment.QuotedReceiveValue, payment.QuotedReceiveAssetCode, payment.QuotedReceiveAssetScale) =
                (quoted.Value, quoted.AssetCode, (short)quoted.AssetScale);
        payment.ErrorCode = reading.FailureCode;
        payment.ErrorMessage = reading.FailureDetail;
    }
}
