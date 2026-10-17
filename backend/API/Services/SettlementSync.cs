using System.Globalization;
using Relia.Api.Domain;
using Relia.Api.Rail;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Sincronizador de liquidaciones con su pago de riel: pending → paid | voided. Lo usan la creación (con la
/// respuesta de POST /payments), la lectura (refresco) y el barrido de fondo.
/// </summary>
public interface ISettlementSync
{
    /// <summary>
    /// Aplica una lectura del proxy. Solo dentro de una transacción, con la liquidación y el pago recién creados o
    /// bloqueados (FOR UPDATE). Descarta lecturas con version ≤ rail_version.
    /// </summary>
    Task ApplyAsync(Settlement settlement, Payment payment, RailPayment reading, string action,
        CancellationToken ct = default);

    /// <summary>
    /// Marca la liquidación pagada sin lectura del riel (billetera simulada) o con la lectura completed, en el orden
    /// que exigen los triggers. Solo dentro de una transacción.
    /// </summary>
    Task PayAsync(Settlement settlement, Payment payment, DateTimeOffset paidAt, CancellationToken ct = default);

    /// <summary>
    /// Anula la liquidación sin movimiento: el pago queda en <paramref name="paymentState"/> (failed, expired o
    /// cancelled) y la comisión sigue acumulada. Solo dentro de una transacción.
    /// </summary>
    Task VoidAsync(Settlement settlement, Payment payment, OperationState paymentState, string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Consulta el pago en el proxy y lo aplica, como máximo una vez por segundo por pago. Si el proxy no responde,
    /// la liquidación sigue pendiente y se reintenta en la siguiente lectura o en el siguiente barrido.
    /// </summary>
    Task RefreshAsync(Settlement settlement, CancellationToken ct = default);
}

public class SettlementSync(
    ISettlementRepository settlements, IPaymentRepository payments, IAgentRepository agents, IRailClient rail,
    ILedger ledger, ISimulatedInbox inbox, IUnitOfWork unitOfWork, RailRefreshThrottle throttle, TimeProvider time,
    ILogger<SettlementSync> logger) : ISettlementSync
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    public async Task RefreshAsync(Settlement settlement, CancellationToken ct = default)
    {
        if (settlement.State != SettlementState.Pending) return;
        var payment = await payments.GetLatestBySettlementAsync(settlement.Id, ct);
        if (payment?.RailPaymentId is not { } railPaymentId || !throttle.TryAcquire(payment.Id)) return;

        // La consulta al proxy va fuera de la transacción, para no tener filas bloqueadas mientras responde.
        RailPayment reading;
        try
        {
            reading = await rail.GetPaymentAsync(railPaymentId, ct);
        }
        catch (RailException e)
        {
            logger.LogWarning(e, "No se pudo refrescar el pago {PaymentId} de {Reference}", payment.Id, settlement.Reference);
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
            var locked = await settlements.GetForUpdateAsync(settlement.Id, tx);
            var lockedPayment = await payments.GetLatestBySettlementForUpdateAsync(settlement.Id, tx);
            if (locked is null || lockedPayment?.Id != payment.Id) return false;
            await ApplyAsync(locked, lockedPayment, reading, "refresh", tx);
            return true;
        }, ct);
        if (settlement.State != SettlementState.Pending) throttle.Forget(payment.Id);
    }

    public async Task ApplyAsync(Settlement settlement, Payment payment, RailPayment reading, string action,
        CancellationToken ct = default)
    {
        // Misma traducción que las operaciones: completed → paid; failed, rejected, expired o cancelled → voided.
        var (state, _) = RailStatusTranslation.ToOperation(reading.Status);
        var stale = payments.LogReading(payment, reading, action, state, time.GetUtcNow());
        if (!stale) payment.CopyRailData(reading);
        // Solo el riel cierra una liquidación: si ya no está pendiente, la lectura solo queda en la bitácora.
        if (stale || state == OperationState.Pending || settlement.State != SettlementState.Pending)
        {
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        if (state == OperationState.Completed)
        {
            await PayAsync(settlement, payment, reading.CompletedAt ?? time.GetUtcNow(), ct);
            return;
        }
        await VoidAsync(settlement, payment, state,
            reading.FailureDetail ?? reading.FailureCode ?? $"El pago terminó en {reading.Status}", ct);
    }

    public async Task VoidAsync(Settlement settlement, Payment payment, OperationState paymentState, string reason,
        CancellationToken ct = default)
    {
        payment.State = paymentState;
        settlement.State = SettlementState.Voided;
        settlement.VoidReason = reason;
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// En el orden que exigen los triggers: (1) pago completed; (2) liquidación paid; (3) movimiento que baja la
    /// comisión acumulada y sube la liquidada; (4) SMS de aviso al corresponsal.
    /// </summary>
    public async Task PayAsync(Settlement settlement, Payment payment, DateTimeOffset paidAt,
        CancellationToken ct = default)
    {
        payment.State = OperationState.Completed;
        payment.CompletedAt = paidAt;
        await unitOfWork.SaveChangesAsync(ct);

        settlement.State = SettlementState.Paid;
        settlement.PaidAt = paidAt;
        await unitOfWork.SaveChangesAsync(ct);

        var balance = await ledger.LockAsync(settlement.AgentId, ct);
        ledger.Post(balance, new LedgerPosting(LedgerEntryKind.Settlement,
            CommissionChange: -settlement.Amount, SettlementId: settlement.Id, PaymentId: payment.Id));

        if (await agents.GetByIdAsync(settlement.AgentId, ct) is { } agent)
            inbox.Send(MessageChannel.Sms, MessageKind.CommissionNotice, agent.Phone,
                $"Relia: te pagamos {settlement.Amount.ToString("C0", Colombia)} de comisión en tu billetera " +
                $"({settlement.Reference}, del {settlement.PeriodFrom:yyyy-MM-dd} al {settlement.PeriodTo:yyyy-MM-dd}).");
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Avanza las liquidaciones pendientes aunque nadie las consulte, con la misma lógica que la lectura. Cada una va
/// en su propio scope, para que un fallo no frene a las demás.
/// </summary>
public class SettlementSweepTask(ISettlementRepository settlements, IServiceScopeFactory scopes,
    ILogger<SettlementSweepTask> logger) : ISweepTask
{
    public string Name => "liquidaciones";

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (var id in await settlements.ListPendingIdsAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<ISettlementRepository>();
                var sync = scope.ServiceProvider.GetRequiredService<ISettlementSync>();
                if (await repository.GetByIdAsync(id, ct) is { } settlement) await sync.RefreshAsync(settlement, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "El barrido no pudo avanzar la liquidación {SettlementId}", id);
            }
        }
    }
}
