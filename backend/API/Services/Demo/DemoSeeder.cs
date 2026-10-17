using Microsoft.Extensions.Options;
using Relia.Api.Entities;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;

namespace Relia.Api.Services.Demo;

/// <summary>
/// Datos del demo (FLUJOS.md §11): tarifa $1.000 (500/500), un admin, 4 corresponsales (Rosa en ceros y con
/// billetera real; los otros con billetera simulada y saldos de ejemplo), 3000000001 vinculado a jeffdev y
/// 3000000002 sin llave.
/// </summary>
public class DemoSeeder(IDemoDataRepository data, IUnitOfWork unitOfWork, IOptions<ReliaOptions> relia,
    TimeProvider time)
{
    public const string Password = "demo1234";

    private record SampleAgent(
        string Email, string Name, string Contact, string City, string Address, string Phone, string Wallet,
        long Float, long Cash, long Commission, long Settled);

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var now = time.UtcNow();
        var simulated = relia.Value.SimulatedWalletPrefix;

        data.Add(new Fee { TotalCommission = 1000, ReliaCommission = 500, AgentCommission = 500, ValidFrom = now, CreatedAt = now });
        data.Add(new Admin
        {
            Id = Guid.NewGuid(), Email = "admin@relia.dev", Name = "Admin Relia", Password = Password, Active = true,
            CreatedAt = now, UpdatedAt = now,
        });

        SampleAgent[] samples =
        [
            new("rosa@relia.dev", "Tienda Doña Rosa", "Rosa Gómez", "Medellín", "Cra 45 #10-20", "3100000001",
                "$ilp.interledger-test.dev/rosa-test", 0, 0, 0, 0),
            new("miscelanea@relia.dev", "Miscelánea El Progreso", "Jorge Pérez", "Bogotá", "Cl 80 #20-15",
                "3100000002", simulated + "el-progreso", 800_000, 350_000, 12_500, 40_000),
            new("drogueria@relia.dev", "Droguería San José", "Marta Ruiz", "Cali", "Av 6N #23-40", "3100000003",
                simulated + "san-jose", 1_200_000, 500_000, 21_000, 15_000),
            new("panaderia@relia.dev", "Panadería La Espiga", "Luis Torres", "Barranquilla", "Cl 72 #43-10",
                "3100000004", simulated + "la-espiga", 450_000, 180_000, 6_500, 0),
        ];

        var agents = samples.Select(s => (Sample: s, Agent: new Agent
        {
            Id = Guid.NewGuid(), Email = s.Email, Name = s.Name, ContactPerson = s.Contact, City = s.City,
            Address = s.Address, Phone = s.Phone, PaymentPointer = s.Wallet, Password = Password, Active = true,
            CreatedAt = now, UpdatedAt = now,
        })).ToList();
        foreach (var (_, agent) in agents) data.Add(agent);

        data.Add(new WalletKey
        {
            Id = Guid.NewGuid(), Mobile = "3000000001", PaymentPointer = "$ilp.interledger-test.dev/jeffdev",
            WalletAddressUrl = "https://ilp.interledger-test.dev/jeffdev", LinkedAt = now,
        });

        // En Postgres los saldos los crea un trigger al insertar el corresponsal: primero se guardan los corresponsales.
        await unitOfWork.SaveChangesAsync(ct);

        // Historiales de ejemplo para dos corresponsales: una carga Bre-B completada y una asignación de efectivo.
        foreach (var (sample, agent) in agents.Where(a => a.Sample.Email is "drogueria@relia.dev" or "panaderia@relia.dev"))
            SampleHistory(agent, sample.Float, sample.Cash, now.AddDays(-2));
        await unitOfWork.SaveChangesAsync(ct);

        foreach (var (sample, agent) in agents)
        {
            var balance = await data.EnsureBalanceAsync(agent.Id, ct);
            balance.FloatBalance = sample.Float;
            balance.ExpectedCash = sample.Cash;
            balance.AccruedCommission = sample.Commission;
            balance.SettledCommissionTotal = sample.Settled;
            balance.UpdatedAt = now;
        }
        await unitOfWork.SaveChangesAsync(ct);
    }

    private void SampleHistory(Agent agent, long floatAmount, long cash, DateTime at)
    {
        var op = new Operation
        {
            Id = Guid.NewGuid(), AgentId = agent.Id, Type = OperationType.FloatLoad, LoadMethod = LoadMethod.BreB,
            RequestedAmount = floatAmount, NetAmount = floatAmount, ChargedAmount = floatAmount,
            Status = OperationStatus.Completed, IdempotencyKey = $"seed-{agent.Id}", CreatedAt = at, CompletedAt = at,
            UpdatedAt = at,
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(), OperationId = op.Id, Rail = PaymentRail.BreBSimulated,
            Direction = PaymentDirection.Incoming, Status = OperationStatus.Completed, Amount = floatAmount,
            Currency = "COP", SourcePaymentPointer = agent.PaymentPointer, DestinationPaymentPointer = relia.Value.WalletAddress,
            RailDetails = "{}", CreatedAt = at, CompletedAt = at, UpdatedAt = at,
        };
        data.Add(op);
        data.Add(payment);
        data.Add(new LedgerEntry
        {
            AgentId = agent.Id, Type = LedgerEntryType.FloatLoad, OperationId = op.Id, PaymentId = payment.Id,
            FloatBefore = 0, FloatChange = floatAmount, FloatAfter = floatAmount, CreatedAt = at,
        });
        data.Add(new LedgerEntry
        {
            AgentId = agent.Id, Type = LedgerEntryType.CashAdjustment, FloatBefore = floatAmount, FloatAfter = floatAmount,
            CashChange = cash, CashAfter = cash, Reason = "Asignación de efectivo", CreatedAt = at.AddMinutes(30),
        });
    }
}
