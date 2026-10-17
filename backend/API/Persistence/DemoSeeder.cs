using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relia.Api.Auth;
using Relia.Api.Configuration;
using Relia.Api.Domain;

namespace Relia.Api.Persistence;

/// <summary>
/// Datos iniciales de la demo: tarifa vigente, un admin, los 4 corresponsales de docs/prototype.md §9.4
/// (Rosa en ceros, los demás con historial de ejemplo) y la llave +573000000001 → jeffdev; el +573000000002
/// queda sin llave. Corre al arrancar con la base vacía y en POST /demo/reset.
/// </summary>
/// <remarks>
/// El historial de ejemplo pasa por las mismas reglas de la base que una operación real: pago completado,
/// operación completada (el trigger congela el recibo) y después el movimiento con antes y después.
/// Sus pagos llevan <c>{"seed": true}</c> en rail_details: nunca pasaron por el riel.
/// </remarks>
public class DemoSeeder(
    ReliaDbContext db, IOptions<ReliaOptions> options, TimeProvider clock, ILogger<DemoSeeder> log)
{
    public const string AgentPassword = "demo1234";
    public const string AdminEmail = "admin@relia.co";
    public const string AdminPassword = "admin1234";
    public const string LinkedCustomerPhone = "+573000000001";
    public const string LinkedCustomerWallet = "$ilp.interledger-test.dev/jeffdev";

    private const long FeeTotal = 1000;
    private const long FeeAgent = 500;

    private static readonly DemoAgent[] Agents =
    [
        new("rosa@demo.relia", "Tienda Doña Rosa", "Rosa Gómez", "Bogotá", "Calle 45 # 13-20",
            "+573105550101", "$ilp.interledger-test.dev/rosa-test", WalletSimulated: false, []),
        new("luis@demo.relia", "Droguería San José", "Luis Pérez", "Medellín", "Carrera 70 # 44-12",
            "+573105550102", "$ilp.interledger-test.dev/drogueria-san-jose", WalletSimulated: true,
            [
                new CashStep(400_000), new TopUpStep(1_500_000), new DepositStep(120_000, Round: false),
                new DepositStep(50_000, Round: true), new WithdrawalStep(80_000, Round: false),
                new DepositStep(200_000, Round: false), new WithdrawalStep(60_000, Round: true),
            ]),
        new("diana@demo.relia", "Minimercado El Paisa", "Diana Ríos", "Cali", "Calle 5 # 38-40",
            "+573105550103", "$ilp.interledger-test.dev/minimercado-el-paisa", WalletSimulated: true,
            [
                new CashStep(250_000), new TopUpStep(800_000), new DepositStep(70_000, Round: true),
                new WithdrawalStep(150_000, Round: false), new DepositStep(30_000, Round: false),
            ]),
        new("jorge@demo.relia", "Papelería La 70", "Jorge Mora", "Bogotá", "Calle 70 # 11-25",
            "+573105550104", "$ilp.interledger-test.dev/papeleria-la-70", WalletSimulated: true,
            [
                new CashStep(150_000), new TopUpStep(500_000), new WithdrawalStep(40_000, Round: false),
                new DepositStep(90_000, Round: true),
            ]),
    ];

    /// <summary>Siembra solo si no hay admins (base recién creada).</summary>
    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if (await db.Admins.AnyAsync(ct)) return;
        await SeedAsync(ct);
    }

    /// <summary>Siembra sobre tablas vacías, en una transacción.</summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SeedTablesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// POST /demo/reset: vacía todas las tablas con TRUNCATE … RESTART IDENTITY CASCADE (los triggers de
    /// inmutabilidad son por fila y no lo bloquean; las referencias OP-, LQ- y los códigos vuelven a empezar) y
    /// vuelve a sembrar, todo en una transacción. Los ids de admin y corresponsales cambian: hay que volver a entrar.
    /// </summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE admins, agents, agent_balances, fees, wallet_keys, otp_challenges, operations, operation_events,
                     payments, payment_events, ledger_entries, receipts, receipt_issuances, settlements,
                     simulated_messages, idempotency_records
            RESTART IDENTITY CASCADE
            """, ct);
        // Lo que EF tenía cargado ya no existe.
        db.ChangeTracker.Clear();
        await SeedTablesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task SeedTablesAsync(CancellationToken ct)
    {
        var reliaWallet = options.Value.WalletAddress;
        if (string.IsNullOrWhiteSpace(reliaWallet))
            throw new InvalidOperationException("Falta Relia:WalletAddress (payment pointer de Relia) para sembrar.");

        var now = clock.GetUtcNow();

        var fee = new Fee { Total = FeeTotal, ReliaShare = FeeTotal - FeeAgent, AgentShare = FeeAgent, ValidFrom = now.AddDays(-30) };
        db.Fees.Add(fee);
        db.Admins.Add(new Admin
        {
            Id = Guid.NewGuid(), Email = AdminEmail, Name = "Admin Relia",
            Password = PasswordHashing.Hash(AdminPassword), Active = true,
        });
        var customerKey = new WalletKey
        {
            Id = Guid.NewGuid(), Phone = LinkedCustomerPhone, WalletAddress = LinkedCustomerWallet,
            LinkedAt = now.AddDays(-10),
        };
        db.WalletKeys.Add(customerKey);

        var agentPassword = PasswordHashing.Hash(AgentPassword);
        var agents = Agents.Select(a => new Agent
        {
            Id = Guid.NewGuid(), Email = a.Email, Name = a.Name, ManagerName = a.ManagerName, City = a.City,
            Address = a.Address, Phone = a.Phone, WalletAddress = a.Wallet, WalletSimulated = a.WalletSimulated,
            Password = agentPassword, Active = true,
        }).ToList();
        db.Agents.AddRange(agents);
        await db.SaveChangesAsync(ct);

        // El trigger tg_agents_initial_balance ya creó una fila de agent_balances en ceros por corresponsal.
        for (var i = 0; i < agents.Count; i++)
        {
            var balance = await db.AgentBalances.SingleAsync(b => b.AgentId == agents[i].Id, ct);
            var history = new HistoryWriter(db, agents[i], balance, fee, customerKey, reliaWallet);
            var at = now.AddDays(-6).AddHours(i);
            foreach (var step in Agents[i].History)
            {
                await history.ApplyAsync(step, at, ct);
                at = at.AddHours(7);
            }
        }

        log.LogInformation("Base sembrada: {Agents} corresponsales, admin {Admin}", agents.Count, AdminEmail);
    }

    private record DemoAgent(
        string Email, string Name, string ManagerName, string City, string Address, string Phone, string Wallet,
        bool WalletSimulated, HistoryStep[] History);

    private abstract record HistoryStep;
    private record CashStep(long Amount) : HistoryStep;
    private record TopUpStep(long Amount) : HistoryStep;
    private record DepositStep(long RequestedAmount, bool Round) : HistoryStep;
    private record WithdrawalStep(long RequestedAmount, bool Round) : HistoryStep;

    /// <summary>Escribe el historial de un corresponsal y mantiene su fila de agent_balances.</summary>
    private class HistoryWriter(
        ReliaDbContext db, Agent agent, AgentBalance balance, Fee fee, WalletKey customer, string reliaWallet)
    {
        private int _sequence;

        public Task ApplyAsync(HistoryStep step, DateTimeOffset at, CancellationToken ct) => step switch
        {
            CashStep s => AddCashAsync(s.Amount, at, ct),
            TopUpStep s => AddOperationAsync(OperationKind.QuotaTopUp, s.Amount, round: false, at, ct),
            DepositStep s => AddOperationAsync(OperationKind.Deposit, s.RequestedAmount, s.Round, at, ct),
            WithdrawalStep s => AddOperationAsync(OperationKind.Withdrawal, s.RequestedAmount, s.Round, at, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };

        private async Task AddCashAsync(long amount, DateTimeOffset at, CancellationToken ct)
        {
            db.LedgerEntries.Add(Entry(LedgerEntryKind.CashAdjustment, 0, 0, amount, at,
                reason: "Asignación de efectivo"));
            balance.CashOnHand += amount;
            await db.SaveChangesAsync(ct);
        }

        private async Task AddOperationAsync(
            OperationKind kind, long requested, bool round, DateTimeOffset at, CancellationToken ct)
        {
            var isTopUp = kind == OperationKind.QuotaTopUp;
            var feeTotal = isTopUp ? 0 : fee.Total;
            var net = round || isTopUp ? requested : requested - feeTotal;
            var charged = net + feeTotal;
            var completedAt = at.AddSeconds(8);
            var seedKey = $"seed-{agent.Id:N}-{++_sequence}";

            var operation = new Operation
            {
                Id = Guid.NewGuid(), AgentId = agent.Id, Kind = kind,
                TopUpMethod = isTopUp ? TopUpMethodKind.BreB : null,
                WalletKeyId = isTopUp ? null : customer.Id,
                RequestedAmount = requested, RoundAmount = round,
                FeeId = isTopUp ? null : fee.Id,
                FeeTotal = feeTotal, FeeRelia = isTopUp ? 0 : fee.ReliaShare, FeeAgent = isTopUp ? 0 : fee.AgentShare,
                NetAmount = net, ChargedAmount = charged, State = OperationState.Pending,
                IdempotencyKey = seedKey, CreatedAt = at,
            };
            // Depósito: Relia paga el neto al usuario. Retiro: el usuario paga lo cobrado a Relia.
            // Carga Bre-B: el corresponsal paga a Relia por el riel simulado.
            var (source, destination, direction, amount) = kind switch
            {
                OperationKind.Deposit => (reliaWallet, customer.WalletAddress, PaymentDirection.Outgoing, net),
                OperationKind.Withdrawal => (customer.WalletAddress, reliaWallet, PaymentDirection.Incoming, charged),
                _ => (agent.WalletAddress, reliaWallet, PaymentDirection.Incoming, charged),
            };
            var payment = new Payment
            {
                Id = Guid.NewGuid(), OperationId = operation.Id,
                Rail = isTopUp ? PaymentRail.BreBSimulated : PaymentRail.OpenPayments,
                Direction = direction, State = OperationState.Completed, Amount = amount,
                SourceWalletAddress = source, DestinationWalletAddress = destination,
                RailReference = seedKey, RailDetail = JsonDocument.Parse("""{"seed": true}"""),
                CreatedAt = at, CompletedAt = completedAt,
            };
            db.Operations.Add(operation);
            db.Payments.Add(payment);
            db.OperationEvents.Add(Event(operation.Id, null, OperationState.Pending, at));
            await db.SaveChangesAsync(ct);

            operation.State = OperationState.Completed;
            operation.CompletedAt = completedAt;
            db.OperationEvents.Add(Event(operation.Id, OperationState.Pending, OperationState.Completed, completedAt));
            await db.SaveChangesAsync(ct);

            // Variaciones que valida validate_ledger_entry_payment (ver ck_ledger_signs).
            var (quotaChange, cashChange) = kind switch
            {
                OperationKind.Deposit => (-charged, charged),
                OperationKind.Withdrawal => (net, -net),
                _ => (net, 0L),
            };
            var entry = Entry(kind switch
            {
                OperationKind.Deposit => LedgerEntryKind.Deposit,
                OperationKind.Withdrawal => LedgerEntryKind.Withdrawal,
                _ => LedgerEntryKind.QuotaTopUp,
            }, quotaChange, operation.FeeAgent, cashChange, completedAt);
            entry.OperationId = operation.Id;
            entry.PaymentId = payment.Id;
            db.LedgerEntries.Add(entry);
            balance.Quota += quotaChange;
            balance.CashOnHand += cashChange;
            balance.AccruedCommission += operation.FeeAgent;
            await db.SaveChangesAsync(ct);
        }

        private LedgerEntry Entry(
            LedgerEntryKind kind, long quotaChange, long commissionChange, long cashChange, DateTimeOffset at,
            string? reason = null) => new()
        {
            AgentId = agent.Id, Kind = kind,
            QuotaBefore = balance.Quota, QuotaChange = quotaChange, QuotaAfter = balance.Quota + quotaChange,
            CommissionBefore = balance.AccruedCommission, CommissionChange = commissionChange,
            CommissionAfter = balance.AccruedCommission + commissionChange,
            CashBefore = balance.CashOnHand, CashChange = cashChange, CashAfter = balance.CashOnHand + cashChange,
            Reason = reason, CreatedAt = at,
        };

        private static OperationEvent Event(
            Guid operationId, OperationState? previous, OperationState next, DateTimeOffset at) => new()
        {
            OperationId = operationId, PreviousState = previous, NewState = next, Origin = EventOrigin.System,
            Detail = "Historial de ejemplo", CreatedAt = at,
        };
    }
}
