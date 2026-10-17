using System.Globalization;
using Relia.Api.Rail;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Operations;

/// <summary>
/// Máquina de estados de la liquidación: pending → paid | voided. Pago de riel completed → paid
/// (baja la comisión acumulada, sube la liquidada, SMS al corresponsal); failed → voided con el motivo.
/// </summary>
public class SettlementTracker(
    ISettlementRepository settlements, IAgentRepository agents, IRailClient rail, BalanceLedger ledger,
    Messenger messenger, IUnitOfWork unitOfWork, PollThrottle throttle, TimeProvider time,
    ILogger<SettlementTracker> log) : ISweepable
{
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    public async Task RefreshAsync(Db.Settlement settlement, CancellationToken ct = default, bool force = false)
    {
        if (settlement.Status != Db.SettlementStatus.Pending) return;
        var payment = settlement.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        if (payment is null || !Guid.TryParse(payment.RailReference, out var railId)) return;
        if (!force && !throttle.TryAcquire(payment.Id)) return;

        RailPayment? rp;
        try
        {
            rp = await rail.GetPaymentAsync(railId, ct);
        }
        catch (RailUnavailableException e)
        {
            log.LogWarning(e, "No se pudo consultar el pago {PaymentId} de la liquidación", payment.Id);
            return;
        }
        if (rp is null) return;
        PaymentLog.RailResponse(payment, "polled", rp, time.UtcNow());
        if (rp.Version <= OperationTracker.StoredVersion(payment))
        {
            await unitOfWork.SaveChangesAsync(ct);
            return;
        }

        OperationTracker.Record(payment, rp, time.UtcNow());
        switch (rp.Status)
        {
            case RailPaymentStatus.Completed:
                await PayAsync(settlement, payment, Db.EventSource.Rail, ct);
                break;
            case RailPaymentStatus.Failed or RailPaymentStatus.Rejected or RailPaymentStatus.Expired or RailPaymentStatus.Cancelled:
                await VoidAsync(settlement, payment,
                    string.Join(": ", new[] { rp.FailureCode ?? rp.Status.ToString(), rp.FailureDetail }.Where(x => x is not null)),
                    Db.EventSource.Rail, ct);
                break;
            default:
                await unitOfWork.SaveChangesAsync(ct);
                break;
        }
    }

    public async Task SweepAsync(CancellationToken ct = default)
    {
        foreach (var settlement in await settlements.ListPendingTrackedAsync(ct))
            await RefreshAsync(settlement, ct, force: true);
    }

    /// <summary>Mismo orden que la operación: pago completado, liquidación pagada, movimiento del libro.</summary>
    public async Task PayAsync(Db.Settlement settlement, Db.Payment payment, Db.EventSource source,
        CancellationToken ct = default)
    {
        await unitOfWork.InTransactionAsync(async tx =>
        {
            await agents.GetBalanceForUpdateAsync(settlement.AgentId, tx);
            if (await settlements.GetFreshStatusAsync(settlement.Id, tx) != Db.SettlementStatus.Pending) return false;

            var now = time.UtcNow();
            PaymentLog.SetStatus(payment, Db.OperationStatus.Completed, source, "completed", now);
            payment.CompletedAt = now;
            await unitOfWork.SaveChangesAsync(tx);

            settlement.Status = Db.SettlementStatus.Paid;
            settlement.PaidAt = now;
            settlement.UpdatedAt = now;
            await unitOfWork.SaveChangesAsync(tx);

            await ledger.ApplyAsync(new LedgerMove(settlement.AgentId, Db.LedgerEntryType.Settlement, 0,
                -settlement.Amount, 0, SettlementId: settlement.Id, PaymentId: payment.Id,
                SettledChange: settlement.Amount), tx);
            var agent = settlement.Agent ?? await agents.GetByIdAsync(settlement.AgentId, tx);
            messenger.Sms(agent!.Phone, Db.MessageType.CommissionNotice,
                $"Relia: te pagamos ${settlement.Amount.ToString("N0", Co)} de comisión ({settlement.Reference()}) " +
                $"a tu billetera.");
            return true;
        }, ct);
    }

    public async Task VoidAsync(Db.Settlement settlement, Db.Payment payment, string reason, Db.EventSource source,
        CancellationToken ct = default)
    {
        await unitOfWork.InTransactionAsync(async tx =>
        {
            await agents.GetBalanceForUpdateAsync(settlement.AgentId, tx);
            if (await settlements.GetFreshStatusAsync(settlement.Id, tx) != Db.SettlementStatus.Pending) return false;

            var now = time.UtcNow();
            PaymentLog.SetStatus(payment, Db.OperationStatus.Failed, source, Mapping.PaymentFailed, now);
            payment.ErrorCode = Mapping.PaymentFailed;
            payment.ErrorMessage = reason;
            settlement.Status = Db.SettlementStatus.Voided;
            settlement.VoidReason = reason;
            settlement.UpdatedAt = now;
            return true;
        }, ct);
    }

}
