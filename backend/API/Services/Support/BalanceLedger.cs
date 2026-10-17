using Relia.Api.Repositories;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Support;

/// <summary>Un movimiento del libro: variaciones de cupo, comisión y efectivo, y de dónde salió.</summary>
public record LedgerMove(
    Guid AgentId, Db.LedgerEntryType Type, long FloatChange, long CommissionChange, long CashChange,
    Guid? OperationId = null, Guid? SettlementId = null, Guid? PaymentId = null, string? Reason = null,
    long SettledChange = 0);

/// <summary>
/// Único lugar que cambia cupo, comisión y efectivo. Bloquea la fila de saldos (SELECT ... FOR UPDATE),
/// escribe el movimiento con antes, variación y después, y actualiza los saldos. Debe correr dentro de
/// una transacción (IUnitOfWork.InTransactionAsync).
/// </summary>
public class BalanceLedger(IAgentRepository agents, ILedgerRepository ledger, TimeProvider time)
{
    public async Task<Db.LedgerEntry> ApplyAsync(LedgerMove move, CancellationToken ct = default)
    {
        var balance = await agents.GetBalanceForUpdateAsync(move.AgentId, ct);
        var now = time.UtcNow();
        var entry = new Db.LedgerEntry
        {
            AgentId = move.AgentId,
            Type = move.Type,
            OperationId = move.OperationId,
            SettlementId = move.SettlementId,
            PaymentId = move.PaymentId,
            FloatBefore = balance.FloatBalance,
            FloatChange = move.FloatChange,
            FloatAfter = balance.FloatBalance + move.FloatChange,
            CommissionBefore = balance.AccruedCommission,
            CommissionChange = move.CommissionChange,
            CommissionAfter = balance.AccruedCommission + move.CommissionChange,
            CashBefore = balance.ExpectedCash,
            CashChange = move.CashChange,
            CashAfter = balance.ExpectedCash + move.CashChange,
            Reason = move.Reason,
            CreatedAt = now,
        };
        if (entry.FloatAfter < 0 || entry.CommissionAfter < 0 || entry.CashAfter < 0)
            // Los servicios validan antes; llegar aquí es un error de programación (el esquema también lo impide).
            throw new InvalidOperationException($"El movimiento {move.Type} dejaría un saldo negativo");

        balance.FloatBalance = entry.FloatAfter;
        balance.AccruedCommission = entry.CommissionAfter;
        balance.ExpectedCash = entry.CashAfter;
        balance.SettledCommissionTotal += move.SettledChange;
        balance.UpdatedAt = now;
        ledger.Add(entry);
        return entry;
    }
}
