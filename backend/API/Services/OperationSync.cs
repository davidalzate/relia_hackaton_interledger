using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Relia.Api.Domain;
using Relia.Api.Rail;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Sincronizador de operaciones con su pago de riel: traduce el estado del proxy, descarta lecturas viejas y
/// lleva la operación a su estado final. Lo usan la creación (con la respuesta de POST /payments), la lectura
/// (refresco) y, más adelante, el barrido de fondo.
/// </summary>
public interface IOperationSync
{
    /// <summary>
    /// Aplica una lectura del proxy. Solo dentro de una transacción, con la operación y el pago recién creados o
    /// bloqueados (FOR UPDATE). Descarta lecturas con version ≤ rail_version.
    /// </summary>
    /// <remarks><paramref name="origin"/> queda en el evento de la operación si la lectura la cierra.</remarks>
    Task ApplyAsync(Operation operation, Payment payment, RailPayment rail, string action,
        EventOrigin origin = EventOrigin.Rail, CancellationToken ct = default);

    /// <summary>
    /// Cierra una operación pendiente sin movimiento (cancelada, vencida o fallida) cuando no hay lectura del riel
    /// que aplicar: pagos que no pasan por el proxy (Bre-B simulado) o el vencimiento del backend (ticket 11). La reserva se libera sola. Solo dentro de una transacción.
    /// </summary>
    Task CloseWithoutMovementAsync(Operation operation, Payment payment, OperationState state, string failureCode,
        string? detail, EventOrigin origin, CancellationToken ct = default);

    /// <summary>
    /// Consulta el pago en el proxy y lo aplica, como máximo una vez por segundo por pago. Si el proxy no responde,
    /// la operación sigue igual y se reintenta en la siguiente lectura.
    /// </summary>
    Task RefreshAsync(Operation operation, CancellationToken ct = default);
}

/// <summary>Tabla de traducción del pago de riel a la operación (spec, "Operaciones").</summary>
public static class RailStatusTranslation
{
    public static (OperationState State, string? FailureCode) ToOperation(string railStatus) => railStatus switch
    {
        "pending_approval" or "processing" => (OperationState.Pending, null),
        "completed" => (OperationState.Completed, null),
        "rejected" => (OperationState.Failed, "customer_rejected"),
        "expired" => (OperationState.Expired, "request_expired"),
        "cancelled" => (OperationState.Cancelled, "request_cancelled"),
        "failed" => (OperationState.Failed, "payment_failed"),
        // El enum del proxy es cerrado; ante un estado nuevo, la operación espera en vez de cerrarse mal.
        _ => (OperationState.Pending, null),
    };
}

/// <summary>Último refresco de cada pago, en memoria: máximo una consulta al proxy por segundo por pago.</summary>
public class RailRefreshThrottle(TimeProvider time)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _last = new();

    /// <summary>Pago en estado final: ya no se vuelve a consultar.</summary>
    public void Forget(Guid paymentId) => _last.TryRemove(paymentId, out _);

    public bool TryAcquire(Guid paymentId)
    {
        var now = time.GetUtcNow();
        var acquired = false;
        _last.AddOrUpdate(paymentId,
            _ =>
            {
                acquired = true;
                return now;
            },
            (_, previous) =>
            {
                acquired = now - previous >= Interval;
                return acquired ? now : previous;
            });
        return acquired;
    }
}

public class OperationSync(
    IOperationRepository operations, IPaymentRepository payments, IAgentRepository agents,
    IWalletKeyRepository keys, IRailClient rail, ILedger ledger, ISimulatedInbox inbox, IUnitOfWork unitOfWork,
    RailRefreshThrottle throttle, TimeProvider time, ILogger<OperationSync> logger) : IOperationSync
{
    private static readonly JsonSerializerOptions RailJson = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task RefreshAsync(Operation operation, CancellationToken ct = default)
    {
        if (operation.State != OperationState.Pending) return;
        var payment = await payments.GetLatestByOperationAsync(operation.Id, ct);
        if (payment?.RailPaymentId is not { } railPaymentId || !throttle.TryAcquire(payment.Id)) return;

        // La consulta al proxy va fuera de la transacción, para no tener filas bloqueadas mientras responde.
        RailPayment reading;
        try
        {
            reading = await rail.GetPaymentAsync(railPaymentId, ct);
        }
        catch (RailException e)
        {
            logger.LogWarning(e, "No se pudo refrescar el pago {PaymentId} de {Reference}", payment.Id, operation.Reference);
            payments.AddEvent(new PaymentEvent
            {
                PaymentId = payment.Id, Action = "refresh", PreviousState = payment.State, NewState = payment.State,
                Origin = EventOrigin.Rail, Detail = $"sin respuesta: {e.Code}", CreatedAt = time.GetUtcNow(),
            });
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        await unitOfWork.InTransactionAsync(async tx =>
        {
            var locked = await operations.GetForUpdateAsync(operation.Id, tx);
            var lockedPayment = await payments.GetLatestByOperationForUpdateAsync(operation.Id, tx);
            if (locked is null || lockedPayment?.Id != payment.Id) return false;
            await ApplyAsync(locked, lockedPayment, reading, "refresh", ct: tx);
            return true;
        }, ct);
        if (operation.State != OperationState.Pending) throttle.Forget(payment.Id);
    }

    public async Task ApplyAsync(Operation operation, Payment payment, RailPayment reading, string action,
        EventOrigin origin = EventOrigin.Rail, CancellationToken ct = default)
    {
        var previous = payment.State;
        var (state, failureCode) = RailStatusTranslation.ToOperation(reading.Status);
        var stale = payment.RailVersion is { } processed && reading.Version <= processed;
        // Toda respuesta del riel queda en la bitácora, también las lecturas viejas que se descartan.
        payments.AddEvent(new PaymentEvent
        {
            PaymentId = payment.Id,
            Action = action,
            PreviousState = previous,
            NewState = stale ? previous : state,
            Origin = EventOrigin.Rail,
            RailResponse = JsonSerializer.SerializeToDocument(reading, RailJson),
            Detail = $"{reading.Status} v{reading.Version}{(stale ? " (descartada)" : "")}",
            CreatedAt = time.GetUtcNow(),
        });
        if (stale)
        {
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        CopyRailData(payment, reading);
        if (state != OperationState.Pending && operation.State != OperationState.Pending)
        {
            // La operación ya terminó por otro camino (p. ej. vencida en el backend) y el riel cerró después:
            // el pago refleja lo que pasó en el riel, pero no se mueven saldos. Un completed aquí es dinero que se
            // movió sin operación: queda para soporte.
            payment.State = state;
            if (state == OperationState.Completed) payment.CompletedAt = reading.CompletedAt ?? time.GetUtcNow();
            if (state == OperationState.Completed)
                logger.LogError("El pago {PaymentId} se completó en el riel con la operación {Reference} ya {State}",
                    payment.Id, operation.Reference, operation.State);
        }
        if (state == OperationState.Pending || operation.State != OperationState.Pending)
        {
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        if (state == OperationState.Completed)
            await CompleteAsync(operation, payment, reading.CompletedAt ?? time.GetUtcNow(), ct);
        else
            await CloseWithoutMovementAsync(operation, payment, state, failureCode!, reading.FailureDetail, origin, ct);
    }

    /// <summary>
    /// En el orden que exigen los triggers: (1) pago completed; (2) operación completed, que congela el recibo;
    /// (3) movimiento y saldos; (4) evento y mensaje.
    /// </summary>
    private async Task CompleteAsync(Operation operation, Payment payment, DateTimeOffset completedAt,
        CancellationToken ct)
    {
        var previous = operation.State;
        payment.State = OperationState.Completed;
        payment.CompletedAt = completedAt;
        await unitOfWork.SaveChangesAsync(ct);

        operation.State = OperationState.Completed;
        operation.CompletedAt = completedAt;
        await unitOfWork.SaveChangesAsync(ct);

        var balance = await ledger.LockAsync(operation.AgentId, ct);
        ledger.Post(balance, Posting(operation, payment));

        operations.AddEvent(new OperationEvent
        {
            OperationId = operation.Id,
            PreviousState = previous,
            NewState = OperationState.Completed,
            Origin = EventOrigin.Rail,
            CreatedAt = time.GetUtcNow(),
        });
        if (operation.Kind == OperationKind.Deposit) await SendDepositNoticeAsync(operation, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Fallo, vencimiento o cancelación: la reserva se libera sola (ya no está pendiente).</summary>
    public async Task CloseWithoutMovementAsync(Operation operation, Payment payment, OperationState state,
        string failureCode, string? detail, EventOrigin origin, CancellationToken ct = default)
    {
        var previous = operation.State;
        payment.State = state;
        operation.State = state;
        operation.FailureCode = failureCode;
        operations.AddEvent(new OperationEvent
        {
            OperationId = operation.Id,
            PreviousState = previous,
            NewState = state,
            Origin = origin,
            Detail = detail,
            CreatedAt = time.GetUtcNow(),
        });
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Variación de saldos por tipo (spec, "Variación de saldos por tipo").</summary>
    private static LedgerPosting Posting(Operation o, Payment payment) => o.Kind switch
    {
        OperationKind.Deposit => new LedgerPosting(LedgerEntryKind.Deposit,
            QuotaChange: -o.ChargedAmount, CommissionChange: o.FeeAgent, CashChange: o.ChargedAmount,
            OperationId: o.Id, PaymentId: payment.Id),
        OperationKind.Withdrawal => new LedgerPosting(LedgerEntryKind.Withdrawal,
            QuotaChange: o.NetAmount, CommissionChange: o.FeeAgent, CashChange: -o.NetAmount,
            OperationId: o.Id, PaymentId: payment.Id),
        _ => new LedgerPosting(LedgerEntryKind.QuotaTopUp,
            QuotaChange: o.NetAmount, OperationId: o.Id, PaymentId: payment.Id),
    };

    private async Task SendDepositNoticeAsync(Operation operation, CancellationToken ct)
    {
        var key = await keys.GetByIdAsync(operation.WalletKeyId!.Value, ct);
        var agent = await agents.GetByIdAsync(operation.AgentId, ct);
        if (key is null || agent is null) return;
        inbox.Send(MessageChannel.Sms, MessageKind.DepositNotice, key.Phone,
            $"Relia: recibiste {operation.NetAmount.ToString("C0", Colombia)} en tu billetera por un depósito en " +
            $"{agent.Name} ({operation.Reference}).",
            operationId: operation.Id);
    }

    private static void CopyRailData(Payment payment, RailPayment reading)
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
        payment.ErrorCode = reading.FailureCode;
        payment.ErrorMessage = reading.FailureDetail;
    }
}
