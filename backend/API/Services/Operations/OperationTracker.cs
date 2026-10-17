using System.Globalization;
using System.Text.Json;
using Relia.Api.Rail;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Operations;

/// <summary>
/// Dueño de la máquina de estados de la operación: pending → completed | failed | expired | cancelled.
/// Lee el pago de riel (máx. una vez por segundo por pago, descartando versiones viejas), traduce su estado
/// (FLUJOS.md, Anexo) y aplica la transición. Al completar, en una transacción y en este orden (lo exigen los
/// triggers de Postgres): pago completado, operación completada, movimiento del libro, recibo.
/// </summary>
public class OperationTracker(
    IOperationRepository operations, IAgentRepository agents, IReceiptRepository receipts, IRailClient rail,
    BalanceLedger ledger, Messenger messenger, IUnitOfWork unitOfWork, PollThrottle throttle, TimeProvider time,
    ILogger<OperationTracker> log)
{
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>Trae el último estado del riel y vence la solicitud si pasó su plazo. Solo actúa si está pending.</summary>
    public async Task RefreshAsync(Db.Operation op, CancellationToken ct = default, bool force = false)
    {
        if (op.Status != Db.OperationStatus.Pending || op.LatestPayment() is not { } payment) return;

        if (payment.Rail == Db.PaymentRail.OpenPayments
            && Guid.TryParse(payment.RailReference, out var railId)
            && (force || throttle.TryAcquire(payment.Id)))
        {
            try
            {
                if (await rail.GetPaymentAsync(railId, ct) is { } railPayment)
                    await ApplyAsync(op, payment, railPayment, ct);
            }
            catch (RailUnavailableException e)
            {
                log.LogWarning(e, "No se pudo consultar el pago {PaymentId} en el riel", payment.Id);
            }
        }

        if (op.Status == Db.OperationStatus.Pending) await ExpireIfDueAsync(op, payment, ct);
    }

    /// <summary>Aplica una lectura del riel. Descarta las que traen una versión que ya se procesó.</summary>
    public async Task ApplyAsync(Db.Operation op, Db.Payment payment, RailPayment rp, CancellationToken ct = default)
    {
        // Cada respuesta queda en la bitácora del pago, aunque sea vieja o no cambie el estado.
        PaymentLog.RailResponse(payment, "polled", rp, time.UtcNow());
        if (rp.Version <= StoredVersion(payment))
        {
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }
        Record(payment, rp, time.UtcNow());

        switch (rp.Status)
        {
            case RailPaymentStatus.Completed:
                await CompleteAsync(op, payment, Db.EventSource.Rail, ct);
                break;
            case RailPaymentStatus.Rejected:
                await FinishAsync(op, payment, Db.OperationStatus.Failed, Mapping.CustomerRejected, rp.FailureDetail, Db.EventSource.Rail, ct);
                break;
            case RailPaymentStatus.Expired:
                await FinishAsync(op, payment, Db.OperationStatus.Expired, Mapping.RequestExpired, rp.FailureDetail, Db.EventSource.Rail, ct);
                break;
            case RailPaymentStatus.Cancelled:
                await FinishAsync(op, payment, Db.OperationStatus.Cancelled, Mapping.RequestCancelled, rp.FailureDetail, Db.EventSource.Rail, ct);
                break;
            case RailPaymentStatus.Failed:
                await FinishAsync(op, payment, Db.OperationStatus.Failed, Mapping.PaymentFailed,
                    string.Join(": ", new[] { rp.FailureCode, rp.FailureDetail }.Where(x => x is not null)), Db.EventSource.Rail, ct);
                break;
            default:
                await unitOfWork.SaveChangesAsync(ct);
                break;
        }
    }

    /// <summary>
    /// El backend es dueño del plazo: pasado expiresAt, cancela en el riel y deja la operación expired.
    /// Si el pagador ya aprobó (no cancelable) o el riel no responde, la operación sigue pending.
    /// </summary>
    public async Task ExpireIfDueAsync(Db.Operation op, Db.Payment payment, CancellationToken ct = default)
    {
        if (payment.ExpiresAt is null || time.UtcNow() < payment.ExpiresAt) return;
        await CancelPendingAsync(op, Db.OperationStatus.Expired, "Venció el plazo de aprobación",
            Db.EventSource.System, ct);
    }

    public enum CancelResult { Cancelled, NotCancellable, RailUnavailable }

    /// <summary>
    /// Cancela en el riel un retiro o una carga pending y la cierra con el estado dado (cancelled o expired).
    /// Lo usan el corresponsal (cancelar) y el Modo demo (vencer ya).
    /// </summary>
    public async Task<CancelResult> CancelPendingAsync(Db.Operation op, Db.OperationStatus status, string detail,
        Db.EventSource source, CancellationToken ct = default)
    {
        var payment = op.LatestPayment()!;
        var result = await TryCancelAtRailAsync(payment, ct);
        if (result == CancelResult.Cancelled)
            await FinishAsync(op, payment, status,
                status == Db.OperationStatus.Expired ? Mapping.RequestExpired : Mapping.RequestCancelled,
                detail, source, ct);
        return result;
    }

    /// <summary>Cancela el cobro en el riel. Sin cobro de riel (Bre-B simulado), no hay nada que cancelar.</summary>
    public async Task<CancelResult> TryCancelAtRailAsync(Db.Payment payment, CancellationToken ct = default)
    {
        if (payment.Rail != Db.PaymentRail.OpenPayments || !Guid.TryParse(payment.RailReference, out var railId))
            return CancelResult.Cancelled;
        try
        {
            var cancelled = await rail.CancelPaymentAsync(railId, ct);
            PaymentLog.RailResponse(payment, "cancelled", cancelled, time.UtcNow());
            if (cancelled.Version > StoredVersion(payment)) Record(payment, cancelled, time.UtcNow());
            return CancelResult.Cancelled;
        }
        catch (RailPaymentNotCancellableException)
        {
            return CancelResult.NotCancellable;
        }
        catch (RailUnavailableException e)
        {
            log.LogWarning(e, "No se pudo cancelar el pago {PaymentId} en el riel", payment.Id);
            return CancelResult.RailUnavailable;
        }
    }

    public async Task CompleteAsync(Db.Operation op, Db.Payment payment, Db.EventSource source,
        CancellationToken ct = default)
    {
        await unitOfWork.InTransactionAsync(async tx =>
        {
            // El bloqueo de saldos serializa las transiciones del corresponsal: nadie completa dos veces.
            await agents.GetBalanceForUpdateAsync(op.AgentId, tx);
            if (await operations.GetFreshStatusAsync(op.Id, tx) != Db.OperationStatus.Pending) return false;

            var now = time.UtcNow();
            PaymentLog.SetStatus(payment, Db.OperationStatus.Completed, source, "completed", now);
            payment.CompletedAt = now;
            await unitOfWork.SaveChangesAsync(tx);

            SetStatus(op, Db.OperationStatus.Completed, source, null, now);
            op.CompletedAt = now;
            await unitOfWork.SaveChangesAsync(tx);

            var (floatChange, commissionChange, cashChange) = Breakdown.Of(op).Changes(op.Type);
            await ledger.ApplyAsync(new LedgerMove(op.AgentId, LedgerType(op.Type), floatChange, commissionChange,
                cashChange, OperationId: op.Id, PaymentId: payment.Id), tx);
            Notify(op);
            await unitOfWork.SaveChangesAsync(tx);

            if (op.Type != Db.OperationType.FloatLoad && await receipts.GetByOperationAsync(op.Id, tx) is null)
            {
                var agent = op.Agent ?? await agents.GetByIdAsync(op.AgentId, tx);
                receipts.Add(ReceiptSnapshot.Of(op, agent!, op.WalletKey!, payment, now).Freeze(op.Id, now));
            }
            return true;
        }, ct);
        await operations.ReloadAsync(op, ct);
    }

    /// <summary>Cierra la operación sin mover saldos; la reserva se libera sola (las reservas salen de las pendientes).</summary>
    public async Task FinishAsync(Db.Operation op, Db.Payment payment, Db.OperationStatus status, string failureCode,
        string? detail, Db.EventSource source, CancellationToken ct = default)
    {
        await unitOfWork.InTransactionAsync(async tx =>
        {
            await agents.GetBalanceForUpdateAsync(op.AgentId, tx);
            if (await operations.GetFreshStatusAsync(op.Id, tx) != Db.OperationStatus.Pending) return false;

            var now = time.UtcNow();
            PaymentLog.SetStatus(payment, status, source, failureCode, now);
            payment.ErrorCode = failureCode;
            payment.ErrorMessage = detail;
            payment.ApprovalUrl = null;
            SetStatus(op, status, source, failureCode, now);
            return true;
        }, ct);
        await operations.ReloadAsync(op, ct);
    }

    private void Notify(Db.Operation op)
    {
        if (op.Type == Db.OperationType.Deposit && op.WalletKey is { } key)
            messenger.Sms(key.Mobile, Db.MessageType.DepositNotice,
                $"Relia: recibiste ${op.NetAmount.ToString("N0", Co)} en tu billetera por un depósito en " +
                $"{op.Agent?.Name}. Ref {op.Reference()}.", operationId: op.Id);
    }

    private void SetStatus(Db.Operation op, Db.OperationStatus status, Db.EventSource source, string? detail, DateTime now)
    {
        operations.AddEvent(new Db.OperationEvent
        {
            OperationId = op.Id, PreviousStatus = op.Status, NewStatus = status, Source = source, Details = detail,
            CreatedAt = now,
        });
        op.Status = status;
        op.UpdatedAt = now;
    }


    public static Db.LedgerEntryType LedgerType(Db.OperationType type) => type switch
    {
        Db.OperationType.Deposit => Db.LedgerEntryType.Deposit,
        Db.OperationType.Withdrawal => Db.LedgerEntryType.Withdrawal,
        _ => Db.LedgerEntryType.FloatLoad,
    };

    // ---- Lo que el riel informa, guardado en el pago ----

    /// <summary>La última versión del riel ya procesada vive en payments.rail_details ({"version": n}).</summary>
    public static int StoredVersion(Db.Payment payment)
    {
        try
        {
            using var json = JsonDocument.Parse(string.IsNullOrEmpty(payment.RailDetails) ? "{}" : payment.RailDetails);
            return json.RootElement.TryGetProperty("version", out var v) && v.TryGetInt32(out var n) ? n : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    public static void Record(Db.Payment payment, RailPayment rp, DateTime now)
    {
        payment.RailReference = rp.Id.ToString();
        payment.RailDetails = JsonSerializer.Serialize(new { version = rp.Version, status = rp.Status.ToString() });
        if (rp.Status == RailPaymentStatus.PendingApproval) payment.ApprovalUrl = rp.ApprovalUrl;
        payment.OpIncomingPaymentUrl = rp.IncomingPaymentUrl ?? payment.OpIncomingPaymentUrl;
        payment.OpQuoteUrl = rp.QuoteUrl ?? payment.OpQuoteUrl;
        payment.OpOutgoingPaymentUrl = rp.OutgoingPaymentUrl ?? payment.OpOutgoingPaymentUrl;
        if (rp.DebitAmount is { } debit) (payment.DebitValue, payment.DebitAssetCode, payment.DebitAssetScale) = (debit.ToString(), "COP", 0);
        if (rp.ReceivedAmount is { } received)
            (payment.ReceivedValue, payment.ReceivedAssetCode, payment.ReceivedAssetScale) = (received.ToString(), "COP", 0);
        payment.UpdatedAt = now;
    }
}
