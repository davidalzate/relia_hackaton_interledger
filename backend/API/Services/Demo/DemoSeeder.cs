using Microsoft.Extensions.Options;
using Relia.Api.Entities;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;

namespace Relia.Api.Services.Demo;

/// <summary>
/// Datos del demo (FLUJOS.md §11), leídos de la configuración "Demo:Seed" (<see cref="DemoSeedOptions"/>):
/// tarifa, admins, corresponsales con sus saldos iniciales y llaves ya vinculadas.
/// </summary>
public class DemoSeeder(IDemoDataRepository data, IUnitOfWork unitOfWork, IOptions<ReliaOptions> relia,
    IOptions<DemoSeedOptions> seed, TimeProvider time)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var now = time.UtcNow();
        var config = seed.Value;

        data.Add(new Fee
        {
            TotalCommission = config.Fee.Total, ReliaCommission = config.Fee.Relia, AgentCommission = config.Fee.Agent,
            ValidFrom = now, CreatedAt = now,
        });
        foreach (var admin in config.Admins)
            data.Add(new Admin
            {
                Id = Guid.NewGuid(), Email = admin.Email, Name = admin.Name, Password = admin.Password, Active = true,
                CreatedAt = now, UpdatedAt = now,
            });

        var agents = config.Agents.Select(s => (Seed: s, Agent: new Agent
        {
            Id = Guid.NewGuid(), Email = s.Email, Name = s.Name, ContactPerson = s.ContactPerson, City = s.City,
            Address = s.Address, Phone = s.Phone, PaymentPointer = s.PaymentPointer, Password = s.Password,
            Active = true, CreatedAt = now, UpdatedAt = now,
        })).ToList();
        foreach (var (_, agent) in agents) data.Add(agent);

        foreach (var key in config.WalletKeys)
            data.Add(new WalletKey
            {
                Id = Guid.NewGuid(), Mobile = key.Mobile, PaymentPointer = key.PaymentPointer,
                WalletAddressUrl = key.WalletAddressUrl, LinkedAt = now,
            });

        // En Postgres los saldos los crea un trigger al insertar el corresponsal: primero se guardan los corresponsales.
        await unitOfWork.SaveChangesAsync(ct);

        foreach (var (s, agent) in agents.Where(a => a.Seed.SampleHistory))
            SampleHistory(agent, s.Quota, s.Cash, now.AddDays(-2));
        await unitOfWork.SaveChangesAsync(ct);

        foreach (var (s, agent) in agents)
        {
            var balance = await data.EnsureBalanceAsync(agent.Id, ct);
            balance.FloatBalance = s.Quota;
            balance.ExpectedCash = s.Cash;
            balance.AccruedCommission = s.AccruedCommission;
            balance.SettledCommissionTotal = s.SettledCommissionTotal;
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
