using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Variación de saldos que registra un movimiento. Los signos los valida la base (ck_ledger_signs y
/// validate_ledger_entry_payment): ver la tabla de la spec, "Variación de saldos por tipo".
/// </summary>
public record LedgerPosting(
    LedgerEntryKind Kind,
    long QuotaChange = 0,
    long CommissionChange = 0,
    long CashChange = 0,
    string? Reason = null,
    Guid? OperationId = null,
    Guid? SettlementId = null,
    Guid? PaymentId = null);

/// <summary>
/// Único lugar que cambia agent_balances y escribe ledger_entries. Se usa dentro de una transacción:
/// primero <see cref="LockAsync"/> y después <see cref="Post"/>; los cambios se guardan al confirmar.
/// </summary>
public interface ILedger
{
    /// <summary>Bloquea la fila de saldos del corresponsal (FOR UPDATE). 404 not_found si no existe.</summary>
    Task<AgentBalance> LockAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Aplica la variación a la fila bloqueada y agrega el movimiento con antes y después.</summary>
    LedgerEntry Post(AgentBalance locked, LedgerPosting posting);
}

public class Ledger(IAgentRepository agents, ILedgerRepository entries, TimeProvider clock) : ILedger
{
    public async Task<AgentBalance> LockAsync(Guid agentId, CancellationToken ct = default) =>
        await agents.GetBalanceForUpdateAsync(agentId, ct) ?? throw ApiErrors.NotFound();

    public LedgerEntry Post(AgentBalance locked, LedgerPosting posting)
    {
        var entry = new LedgerEntry
        {
            AgentId = locked.AgentId,
            Kind = posting.Kind,
            OperationId = posting.OperationId,
            SettlementId = posting.SettlementId,
            PaymentId = posting.PaymentId,
            QuotaBefore = locked.Quota,
            QuotaChange = posting.QuotaChange,
            QuotaAfter = locked.Quota + posting.QuotaChange,
            CommissionBefore = locked.AccruedCommission,
            CommissionChange = posting.CommissionChange,
            CommissionAfter = locked.AccruedCommission + posting.CommissionChange,
            CashBefore = locked.CashOnHand,
            CashChange = posting.CashChange,
            CashAfter = locked.CashOnHand + posting.CashChange,
            Reason = posting.Reason,
            CreatedAt = clock.GetUtcNow(),
        };

        locked.Quota = entry.QuotaAfter;
        locked.AccruedCommission = entry.CommissionAfter;
        locked.CashOnHand = entry.CashAfter;
        // La liquidación pasa la comisión acumulada a la ya liquidada.
        if (posting.Kind == LedgerEntryKind.Settlement) locked.SettledCommissionTotal -= posting.CommissionChange;

        entries.Add(entry);
        return entry;
    }
}
