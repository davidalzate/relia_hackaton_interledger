using Microsoft.EntityFrameworkCore;
using Npgsql;
using Relia.Api.Domain;
using Relia.Api.Persistence;

namespace Relia.Api.Repositories;

public class EfUnitOfWork(ReliaDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        // Si ya hay una transacción abierta (la del filtro de idempotencia), se trabaja dentro de ella:
        // quien la abrió decide si confirma, para que la creación y su registro sean atómicos.
        if (db.Database.CurrentTransaction is not null)
        {
            var joined = await work(ct);
            await db.SaveChangesAsync(ct);
            return joined;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await work(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }
}

public class EfAgentRepository(ReliaDbContext db) : IAgentRepository
{
    public Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Agents.Include(a => a.Balance).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.Agents.FirstOrDefaultAsync(a => a.Email.ToLower() == email.ToLower(), ct);

    public async Task<AgentBalance?> GetBalanceForUpdateAsync(Guid agentId, CancellationToken ct = default)
    {
        // Si la fila ya estaba en el change tracker, EF devuelve esa instancia sin pisar sus valores:
        // se recarga para decidir sobre lo que hay en la base después de tomar el bloqueo.
        var balance = await db.AgentBalances
            .FromSqlInterpolated($"SELECT * FROM agent_balances WHERE agent_id = {agentId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (balance is not null) await db.Entry(balance).ReloadAsync(ct);
        return balance;
    }

    public Task<bool> UsesPhoneOrWalletAsync(string phone, string walletAddress, CancellationToken ct = default)
    {
        var wallet = walletAddress.TrimEnd('/').ToLowerInvariant();
        return db.Agents.AnyAsync(a => a.Phone == phone || a.WalletAddress.ToLower().TrimEnd('/') == wallet, ct);
    }
}

public class EfAdminRepository(ReliaDbContext db) : IAdminRepository
{
    public Task<Admin?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Admins.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Admin?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.Admins.FirstOrDefaultAsync(a => a.Email.ToLower() == email.ToLower(), ct);
}

public class EfFeeRepository(ReliaDbContext db) : IFeeRepository
{
    public Task<Fee> GetCurrentAsync(CancellationToken ct = default) =>
        db.Fees.SingleAsync(f => f.ValidTo == null, ct);
}

public class EfWalletKeyRepository(ReliaDbContext db) : IWalletKeyRepository
{
    public Task<WalletKey?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
        db.WalletKeys.FirstOrDefaultAsync(k => k.Phone == phone, ct);

    public Task<bool> IsWalletLinkedAsync(string walletAddress, CancellationToken ct = default)
    {
        var wallet = walletAddress.TrimEnd('/').ToLowerInvariant();
        return db.WalletKeys.AnyAsync(k => k.WalletAddress.ToLower().TrimEnd('/') == wallet, ct);
    }

    public void Add(WalletKey key) => db.WalletKeys.Add(key);
}

public class EfOtpChallengeRepository(ReliaDbContext db) : IOtpChallengeRepository
{
    public void Add(OtpChallenge challenge) => db.OtpChallenges.Add(challenge);

    public async Task<OtpChallenge?> GetForUpdateAsync(Guid id, OtpPurpose purpose, CancellationToken ct = default)
    {
        var challenge = await db.OtpChallenges
            .FromSqlInterpolated($"SELECT * FROM otp_challenges WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (challenge is null || challenge.Purpose != purpose) return null;
        await db.Entry(challenge).ReloadAsync(ct);
        return challenge;
    }

    public Task<bool> HasNewerAsync(OtpChallenge challenge, CancellationToken ct = default) =>
        db.OtpChallenges.AnyAsync(c => c.Phone == challenge.Phone && c.Purpose == challenge.Purpose
                                       && c.CreatedAt > challenge.CreatedAt, ct);
}

public class EfOperationRepository(ReliaDbContext db) : IOperationRepository
{
    public Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Operations.Include(o => o.WalletKey).FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Operation?> GetByIdempotencyKeyAsync(Guid agentId, string idempotencyKey,
        CancellationToken ct = default) =>
        db.Operations.FirstOrDefaultAsync(o => o.AgentId == agentId && o.IdempotencyKey == idempotencyKey, ct);

    public async Task<Reservations> GetReservationsAsync(Guid agentId, CancellationToken ct = default)
    {
        var pending = db.Operations.Where(o => o.AgentId == agentId && o.State == OperationState.Pending);
        var quota = await pending.Where(o => o.Kind == OperationKind.Deposit).SumAsync(o => o.ChargedAmount, ct);
        var cash = await pending.Where(o => o.Kind == OperationKind.Withdrawal).SumAsync(o => o.NetAmount, ct);
        return new Reservations(quota, cash);
    }

    public void Add(Operation operation) => db.Operations.Add(operation);

    public void AddEvent(OperationEvent operationEvent) => db.OperationEvents.Add(operationEvent);
}

public class EfPaymentRepository(ReliaDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Payment?> GetLatestByOperationAsync(Guid operationId, CancellationToken ct = default) =>
        db.Payments.Where(p => p.OperationId == operationId)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

    public Task<Payment?> GetLatestBySettlementAsync(Guid settlementId, CancellationToken ct = default) =>
        db.Payments.Where(p => p.SettlementId == settlementId)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

    public void Add(Payment payment) => db.Payments.Add(payment);

    public void AddEvent(PaymentEvent paymentEvent) => db.PaymentEvents.Add(paymentEvent);
}

public class EfLedgerRepository(ReliaDbContext db) : ILedgerRepository
{
    public Task<LedgerEntry?> GetByOperationAsync(Guid operationId, CancellationToken ct = default) =>
        db.LedgerEntries.FirstOrDefaultAsync(l => l.OperationId == operationId, ct);

    public Task<LedgerEntry?> GetBySettlementAsync(Guid settlementId, CancellationToken ct = default) =>
        db.LedgerEntries.FirstOrDefaultAsync(l => l.SettlementId == settlementId, ct);

    public Task<LedgerEntry?> GetLatestByAgentAsync(Guid agentId, CancellationToken ct = default) =>
        db.LedgerEntries.Where(l => l.AgentId == agentId).OrderByDescending(l => l.Id).FirstOrDefaultAsync(ct);

    public void Add(LedgerEntry entry) => db.LedgerEntries.Add(entry);
}

/// <summary>
/// Lee v_agent_history con SQL propio: la vista no tiene llave y la paginación compara la tupla
/// (occurred_at, entry_id), con COLLATE "C" para que el orden sea el mismo en WHERE y ORDER BY.
/// </summary>
public class EfHistoryRepository(ReliaDbContext db) : IHistoryRepository
{
    public async Task<IReadOnlyList<HistoryRow>> ListAsync(Guid agentId, HistoryFilter filter,
        CancellationToken ct = default)
    {
        var parameters = new List<NpgsqlParameter>
        {
            new("agent_id", agentId),
            new("limit", filter.Limit),
        };
        var where = new List<string> { "h.agent_id = @agent_id" };
        if (filter.Type is not null)
        {
            where.Add("h.type = @type");
            parameters.Add(new NpgsqlParameter("type", filter.Type));
        }
        if (filter.Status is not null)
        {
            where.Add("h.status::text = @status");
            parameters.Add(new NpgsqlParameter("status", filter.Status));
        }
        if (filter.After is not null)
        {
            where.Add("""(h.occurred_at < @after_at OR (h.occurred_at = @after_at AND h.entry_id < @after_id COLLATE "C"))""");
            parameters.Add(new NpgsqlParameter("after_at", filter.After.OccurredAt));
            parameters.Add(new NpgsqlParameter("after_id", filter.After.EntryId));
        }

        var sql = $"""
            SELECT h.entry_id AS "EntryId", h.type AS "Type", h.reference AS "Reference",
                   h.occurred_at AS "OccurredAt", h.status::text AS "Status", h.amount AS "Amount",
                   h.float_before AS "QuotaBefore", h.float_after AS "QuotaAfter",
                   h.commission_before AS "CommissionBefore", h.commission_after AS "CommissionAfter",
                   h.cash_before AS "CashBefore", h.cash_after AS "CashAfter", h.reason AS "Reason",
                   o.failure_code AS "FailureCode", h.operation_id AS "OperationId",
                   h.settlement_id AS "SettlementId"
            FROM (
                SELECT v.*, COALESCE(v.operation_id::text, v.settlement_id::text, 'cash-' || v.ledger_entry_id)
                           COLLATE "C" AS entry_id
                FROM v_agent_history v
            ) h
            LEFT JOIN operations o ON o.id = h.operation_id
            WHERE {string.Join(" AND ", where)}
            ORDER BY h.occurred_at DESC, h.entry_id DESC
            LIMIT @limit
            """;
        return await db.Database.SqlQueryRaw<HistoryRow>(sql, parameters.ToArray<object>()).ToListAsync(ct);
    }
}

public class EfReceiptRepository(ReliaDbContext db) : IReceiptRepository
{
    public Task<Receipt?> GetByOperationAsync(Guid operationId, CancellationToken ct = default) =>
        db.Receipts.FirstOrDefaultAsync(r => r.OperationId == operationId, ct);

    public void AddIssue(ReceiptIssue issue) => db.ReceiptIssues.Add(issue);
}

public class EfSettlementRepository(ReliaDbContext db) : ISettlementRepository
{
    public Task<Settlement?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Settlements.Include(s => s.Agent).Include(s => s.CreatedByAdmin)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Settlement>> ListAsync(SettlementFilter f, CancellationToken ct = default)
    {
        var q = db.Settlements.Include(s => s.Agent).Include(s => s.CreatedByAdmin).AsQueryable();
        if (f.AgentId is not null) q = q.Where(s => s.AgentId == f.AgentId);
        if (f.State is not null) q = q.Where(s => s.State == f.State);
        if (f.CreatedFrom is not null) q = q.Where(s => s.CreatedAt >= f.CreatedFrom);
        if (f.CreatedTo is not null) q = q.Where(s => s.CreatedAt < f.CreatedTo);
        if (f.Before is not null) q = q.Where(s => s.CreatedAt < f.Before);
        return await q.OrderByDescending(s => s.CreatedAt).Take(f.Limit).ToListAsync(ct);
    }
}

public class EfSimulatedMessageRepository(ReliaDbContext db) : ISimulatedMessageRepository
{
    public void Add(SimulatedMessage message) => db.SimulatedMessages.Add(message);

    public async Task<IReadOnlyList<SimulatedMessage>> ListAsync(string? phone, long? beforeId, int limit,
        CancellationToken ct = default)
    {
        var q = ForPhone(phone);
        if (beforeId is not null) q = q.Where(m => m.Id < beforeId);
        return await q.OrderByDescending(m => m.Id).Take(limit).ToListAsync(ct);
    }

    public Task<int> CountUnreadAsync(string? phone, CancellationToken ct = default) =>
        ForPhone(phone).CountAsync(m => !m.Read, ct);

    public Task MarkReadAsync(string? phone, CancellationToken ct = default) =>
        ForPhone(phone).Where(m => !m.Read).ExecuteUpdateAsync(s => s.SetProperty(m => m.Read, true), ct);

    private IQueryable<SimulatedMessage> ForPhone(string? phone) =>
        phone is null ? db.SimulatedMessages.AsNoTracking()
            : db.SimulatedMessages.AsNoTracking().Where(m => m.Recipient == phone);
}
