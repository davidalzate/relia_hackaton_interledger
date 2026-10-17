using Microsoft.EntityFrameworkCore;
using Relia.Api.Domain;
using Relia.Api.Persistence;

namespace Relia.Api.Repositories;

public class EfUnitOfWork(ReliaDbContext db) : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
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

    public Task<AgentBalance> GetBalanceForUpdateAsync(Guid agentId, CancellationToken ct = default) =>
        db.AgentBalances
            .FromSqlInterpolated($"SELECT * FROM agent_balances WHERE agent_id = {agentId} FOR UPDATE")
            .SingleAsync(ct);
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

    public void Add(WalletKey key) => db.WalletKeys.Add(key);
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

    public async Task<IReadOnlyList<Operation>> ListByAgentAsync(Guid agentId, OperationKind? kind,
        OperationState? state, DateTimeOffset? before, int limit, CancellationToken ct = default)
    {
        var q = db.Operations.Include(o => o.WalletKey).Where(o => o.AgentId == agentId);
        if (kind is not null) q = q.Where(o => o.Kind == kind);
        if (state is not null) q = q.Where(o => o.State == state);
        if (before is not null) q = q.Where(o => o.CreatedAt < before);
        return await q.OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync(ct);
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

    public async Task<IReadOnlyList<LedgerEntry>> ListCashAdjustmentsAsync(Guid agentId, DateTimeOffset? before,
        int limit, CancellationToken ct = default)
    {
        var q = db.LedgerEntries.Where(l => l.AgentId == agentId && l.Kind == LedgerEntryKind.CashAdjustment);
        if (before is not null) q = q.Where(l => l.CreatedAt < before);
        return await q.OrderByDescending(l => l.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public void Add(LedgerEntry entry) => db.LedgerEntries.Add(entry);
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
}
