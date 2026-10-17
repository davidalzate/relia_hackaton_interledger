using Microsoft.EntityFrameworkCore;
using Relia.Api.Entities;
using Relia.Api.Persistence;

namespace Relia.Api.Repositories;

/// <summary>Borrado total de los datos del demo. Solo lo usa POST /demo/reset.</summary>
public interface IDemoDataRepository
{
    /// <summary>Vacía todas las tablas. En Postgres usa TRUNCATE (las tablas de solo inserción no aceptan DELETE).</summary>
    Task WipeAsync(CancellationToken ct = default);

    /// <summary>Agrega entidades nuevas al contexto (la siembra).</summary>
    void Add(object entity);

    /// <summary>
    /// Fila de saldos del corresponsal. En Postgres la crea un trigger al insertar el corresponsal;
    /// si no existe (base en memoria), se crea aquí.
    /// </summary>
    Task<AgentBalance> EnsureBalanceAsync(Guid agentId, CancellationToken ct = default);
}

public class EfDemoDataRepository(ReliaDbContext db) : IDemoDataRepository
{
    private const string TruncateAll =
        "TRUNCATE receipt_issuances, receipts, ledger_entries, payment_events, operation_events, payments, " +
        "settlements, simulated_messages, operations, otp_challenges, wallet_keys, agent_balances, agents, admins, " +
        "fees RESTART IDENTITY CASCADE";

    public async Task WipeAsync(CancellationToken ct = default)
    {
        db.ChangeTracker.Clear();
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlRawAsync(TruncateAll, ct);
            return;
        }
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);
    }

    public void Add(object entity) => db.Add(entity);

    public async Task<AgentBalance> EnsureBalanceAsync(Guid agentId, CancellationToken ct = default)
    {
        var balance = await db.AgentBalances.FirstOrDefaultAsync(b => b.AgentId == agentId, ct);
        if (balance is not null) return balance;
        balance = new AgentBalance { AgentId = agentId, UpdatedAt = DateTime.UtcNow };
        db.AgentBalances.Add(balance);
        return balance;
    }
}
