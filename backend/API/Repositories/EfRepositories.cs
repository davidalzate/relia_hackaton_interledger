using Microsoft.EntityFrameworkCore;
using Relia.Api.Entities;
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
        db.Agents.Include(a => a.AgentBalance).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        db.Agents.FirstOrDefaultAsync(a => a.Email.ToLower() == email.ToLower(), ct);

    // FOR UPDATE solo en Postgres; la base en memoria de las pruebas no tiene SQL. Si la fila ya estaba en
    // seguimiento, EF devuelve la instancia vieja: se recarga para trabajar con los valores bloqueados.
    public async Task<AgentBalance> GetBalanceForUpdateAsync(Guid agentId, CancellationToken ct = default)
    {
        if (!db.Database.IsRelational()) return await db.AgentBalances.SingleAsync(b => b.AgentId == agentId, ct);
        var balance = await db.AgentBalances
            .FromSqlInterpolated($"SELECT * FROM agent_balances WHERE agent_id = {agentId} FOR UPDATE")
            .SingleAsync(ct);
        await db.Entry(balance).ReloadAsync(ct);
        return balance;
    }

    public async Task<IReadOnlyList<Agent>> ListAsync(CancellationToken ct = default) =>
        await db.Agents.Include(a => a.AgentBalance).OrderBy(a => a.Name).ToListAsync(ct);

    public Task<Agent?> GetByPhoneAsync(string phone, CancellationToken ct = default) =>
        db.Agents.FirstOrDefaultAsync(a => a.Phone == phone, ct);

    public Task<bool> AnyWithPaymentPointerAsync(string pointer, CancellationToken ct = default) =>
        db.Agents.AnyAsync(a => a.PaymentPointer == pointer, ct);
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
        db.Fees.SingleAsync(f => f.ValidUntil == null, ct);
}

public class EfWalletKeyRepository(ReliaDbContext db) : IWalletKeyRepository
{
    public Task<WalletKey?> GetByMobileAsync(string mobile, CancellationToken ct = default) =>
        db.WalletKeys.FirstOrDefaultAsync(k => k.Mobile == mobile, ct);

    public Task<bool> AnyWithPaymentPointerAsync(string pointer, CancellationToken ct = default) =>
        db.WalletKeys.AnyAsync(k => k.PaymentPointer == pointer, ct);

    public void Add(WalletKey key) => db.WalletKeys.Add(key);
}

public class EfOtpChallengeRepository(ReliaDbContext db) : IOtpChallengeRepository
{
    public Task<OtpChallenge?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.OtpChallenges.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<OtpChallenge>> ListOpenAsync(string mobile, CancellationToken ct = default) =>
        await db.OtpChallenges
            .Where(c => c.Mobile == mobile && c.Purpose == OtpPurpose.WalletLinking && c.VerifiedAt == null)
            .ToListAsync(ct);

    public void Add(OtpChallenge challenge) => db.OtpChallenges.Add(challenge);
}

public class EfOperationRepository(ReliaDbContext db) : IOperationRepository
{
    public Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Operations.Include(o => o.WalletKey).Include(o => o.Agent).Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task<Operation?> GetByIdempotencyKeyAsync(Guid agentId, string idempotencyKey,
        CancellationToken ct = default) =>
        db.Operations.FirstOrDefaultAsync(o => o.AgentId == agentId && o.IdempotencyKey == idempotencyKey, ct);

    public async Task<Reservations> GetReservationsAsync(Guid agentId, CancellationToken ct = default)
    {
        var pending = db.Operations.Where(o => o.AgentId == agentId && o.Status == OperationStatus.Pending);
        var floatReserved = await pending.Where(o => o.Type == OperationType.Deposit)
            .SumAsync(o => o.ChargedAmount, ct);
        var cashReserved = await pending.Where(o => o.Type == OperationType.Withdrawal)
            .SumAsync(o => o.NetAmount, ct);
        return new Reservations(floatReserved, cashReserved);
    }

    public async Task<IReadOnlyList<Operation>> ListByAgentAsync(Guid agentId, OperationType? type,
        OperationStatus? status, DateTime? before, int limit, CancellationToken ct = default)
    {
        var q = db.Operations.Include(o => o.WalletKey).Include(o => o.Payments).Where(o => o.AgentId == agentId);
        if (type is not null) q = q.Where(o => o.Type == type);
        if (status is not null) q = q.Where(o => o.Status == status);
        if (before is not null) q = q.Where(o => o.CreatedAt < before);
        return await q.OrderByDescending(o => o.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CompletedTotals>> GetCompletedTotalsAsync(CancellationToken ct = default) =>
        await db.Operations
            .Where(o => o.Status == OperationStatus.Completed
                        && (o.Type == OperationType.Deposit || o.Type == OperationType.Withdrawal))
            .GroupBy(o => new { o.AgentId, o.Type })
            .Select(g => new CompletedTotals(g.Key.AgentId, g.Key.Type, g.Count(), g.Sum(o => o.RequestedAmount)))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Operation>> ListPendingAsync(CancellationToken ct = default) =>
        await db.Operations.Include(o => o.Agent).Include(o => o.WalletKey).Include(o => o.Payments)
            .Where(o => o.Status == OperationStatus.Pending).OrderBy(o => o.CreatedAt).ToListAsync(ct);

    public async Task<DateTime?> GetFirstOperationDateAsync(Guid agentId, CancellationToken ct = default) =>
        await db.Operations.Where(o => o.AgentId == agentId).MinAsync(o => (DateTime?)o.CreatedAt, ct);

    public Task<OperationStatus?> GetFreshStatusAsync(Guid id, CancellationToken ct = default) =>
        db.Operations.AsNoTracking().Where(o => o.Id == id).Select(o => (OperationStatus?)o.Status)
            .FirstOrDefaultAsync(ct);

    public async Task ReloadAsync(Operation operation, CancellationToken ct = default)
    {
        await db.Entry(operation).ReloadAsync(ct);
        foreach (var payment in operation.Payments) await db.Entry(payment).ReloadAsync(ct);
    }

    public async Task<IReadOnlyList<OperationEvent>> ListEventsAsync(Guid operationId, CancellationToken ct = default) =>
        await db.OperationEvents.Where(e => e.OperationId == operationId).OrderBy(e => e.Id).ToListAsync(ct);

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

    public async Task<IReadOnlyList<LedgerEntry>> ListCashAdjustmentsAsync(Guid agentId, DateTime? before,
        int limit, CancellationToken ct = default)
    {
        var q = db.LedgerEntries.Where(l => l.AgentId == agentId && l.Type == LedgerEntryType.CashAdjustment);
        if (before is not null) q = q.Where(l => l.CreatedAt < before);
        return await q.OrderByDescending(l => l.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LedgerEntry>> ListByAgentAsync(Guid agentId, CancellationToken ct = default) =>
        await db.LedgerEntries.Where(l => l.AgentId == agentId).OrderBy(l => l.Id).ToListAsync(ct);

    public void Add(LedgerEntry entry) => db.LedgerEntries.Add(entry);
}

public class EfReceiptRepository(ReliaDbContext db) : IReceiptRepository
{
    public Task<Receipt?> GetByOperationAsync(Guid operationId, CancellationToken ct = default) =>
        db.Receipts.FirstOrDefaultAsync(r => r.OperationId == operationId, ct);

    public void AddIssuance(ReceiptIssuance issuance) => db.ReceiptIssuances.Add(issuance);

    public void Add(Receipt receipt) => db.Receipts.Add(receipt);

    public Task<int> CountIssuancesAsync(Guid receiptId, CancellationToken ct = default) =>
        db.ReceiptIssuances.CountAsync(i => i.ReceiptId == receiptId, ct);
}

// AsNoTracking: el scaffold modeló Agent ↔ Settlement como uno a uno (por el índice único parcial de
// "una pendiente por corresponsal"); sin seguimiento, varias liquidaciones del mismo corresponsal no chocan.
public class EfSettlementRepository(ReliaDbContext db) : ISettlementRepository
{
    public Task<Settlement?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Settlements.AsNoTracking().Include(s => s.Agent).Include(s => s.CreatedByAdmin)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Settlement>> ListAsync(SettlementFilter f, CancellationToken ct = default)
    {
        var q = db.Settlements.AsNoTracking().Include(s => s.Agent).Include(s => s.CreatedByAdmin).AsQueryable();
        if (f.AgentId is not null) q = q.Where(s => s.AgentId == f.AgentId);
        if (f.Status is not null) q = q.Where(s => s.Status == f.Status);
        if (f.CreatedFrom is not null) q = q.Where(s => s.CreatedAt >= f.CreatedFrom);
        if (f.CreatedTo is not null) q = q.Where(s => s.CreatedAt < f.CreatedTo);
        if (f.Before is not null) q = q.Where(s => s.CreatedAt < f.Before);
        return await q.OrderByDescending(s => s.CreatedAt).Take(f.Limit).ToListAsync(ct);
    }

    public Task<Settlement?> GetTrackedAsync(Guid id, CancellationToken ct = default) =>
        db.Settlements.Include(s => s.Agent).Include(s => s.CreatedByAdmin).Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<SettlementStatus?> GetFreshStatusAsync(Guid id, CancellationToken ct = default) =>
        db.Settlements.AsNoTracking().Where(s => s.Id == id).Select(s => (SettlementStatus?)s.Status)
            .FirstOrDefaultAsync(ct);

    public Task<bool> HasPendingAsync(Guid agentId, CancellationToken ct = default) =>
        db.Settlements.AnyAsync(s => s.AgentId == agentId && s.Status == SettlementStatus.Pending, ct);

    public Task<Settlement?> GetLastPaidAsync(Guid agentId, CancellationToken ct = default) =>
        db.Settlements.AsNoTracking().Where(s => s.AgentId == agentId && s.Status == SettlementStatus.Paid)
            .OrderByDescending(s => s.PeriodEnd).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Settlement>> ListPendingTrackedAsync(CancellationToken ct = default) =>
        await db.Settlements.Include(s => s.Agent).Include(s => s.CreatedByAdmin).Include(s => s.Payments)
            .Where(s => s.Status == SettlementStatus.Pending).ToListAsync(ct);

    public void Add(Settlement settlement) => db.Settlements.Add(settlement);

    public async Task<IReadOnlyDictionary<Guid, long>> SumPaidSinceAsync(DateTime paidFromUtc,
        CancellationToken ct = default) =>
        await db.Settlements
            .Where(s => s.Status == SettlementStatus.Paid && s.PaidAt >= paidFromUtc)
            .GroupBy(s => s.AgentId)
            .Select(g => new { g.Key, Sum = g.Sum(s => s.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
}

public class EfSimulatedMessageRepository(ReliaDbContext db) : ISimulatedMessageRepository
{
    public void Add(SimulatedMessage message) => db.SimulatedMessages.Add(message);

    public async Task<IReadOnlyList<SimulatedMessage>> ListAsync(string? phone, long? beforeId, int limit,
        CancellationToken ct = default)
    {
        var q = ByPhone(phone);
        if (beforeId is not null) q = q.Where(m => m.Id < beforeId);
        return await q.OrderByDescending(m => m.Id).Take(limit).ToListAsync(ct);
    }

    public Task<int> CountUnreadAsync(string? phone, CancellationToken ct = default) =>
        ByPhone(phone).CountAsync(m => !m.IsRead, ct);

    public async Task MarkReadAsync(string? phone, CancellationToken ct = default)
    {
        foreach (var message in await ByPhone(phone).Where(m => !m.IsRead).ToListAsync(ct)) message.IsRead = true;
    }

    private IQueryable<SimulatedMessage> ByPhone(string? phone) =>
        phone is null ? db.SimulatedMessages : db.SimulatedMessages.Where(m => m.Recipient == phone);
}
