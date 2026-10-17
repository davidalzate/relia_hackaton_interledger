using Microsoft.Extensions.Options;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Rail;
using Relia.Api.Repositories;
using Relia.Api.Services.Demo;
using Relia.Api.Services.Operations;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services;

/// <summary>Ticket 15: liquidaciones de la comisión acumulada a la billetera del corresponsal.</summary>
public class SettlementService(
    ISettlementRepository settlements, IAgentRepository agents, IAdminRepository admins,
    IOperationRepository operations, IPaymentRepository payments, IRailClient rail, SettlementTracker tracker,
    IUnitOfWork unitOfWork, IdempotencyStore idempotency, DemoState demo, IOptions<ReliaOptions> relia,
    TimeProvider time, ICurrentUser currentUser, AgentLocks locks, ILogger<SettlementService> log) : ISettlementService
{
    public Task<SettlementDto> CreateAsync(Guid agentId, string idempotencyKey, CancellationToken ct = default) =>
        idempotency.RunAsync($"{agentId}|settlements", idempotencyKey, agentId, () => SettleAsync(agentId, idempotencyKey, ct));

    /// <summary>
    /// Igual que las operaciones: verifica, pide el pago al riel sin transacción abierta y guarda en una
    /// transacción corta que vuelve a verificar. Un 503 del riel sale sin haber escrito nada.
    /// </summary>
    private async Task<SettlementDto> SettleAsync(Guid agentId, string idempotencyKey, CancellationToken ct)
    {
        using var _ = await locks.AcquireAsync(agentId, ct);
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        var admin = await admins.GetByIdAsync(currentUser.UserId, ct) ?? throw ApiErrors.Unauthorized();
        var now = time.UtcNow();
        var today = time.BogotaToday();

        var settlement = new Db.Settlement
        {
            Id = Guid.NewGuid(), AgentId = agentId, CreatedByAdminId = admin.Id,
            DestinationPaymentPointer = agent.PaymentPointer, PeriodEnd = today, Status = Db.SettlementStatus.Pending,
            CreatedAt = now, UpdatedAt = now,
        };
        var payment = new Db.Payment
        {
            Id = PaymentLog.PaymentId($"{agentId}|settlement", idempotencyKey), SettlementId = settlement.Id, Rail = Db.PaymentRail.OpenPayments,
            Direction = Db.PaymentDirection.Outgoing, Status = Db.OperationStatus.Pending, Currency = "COP",
            SourcePaymentPointer = relia.Value.WalletAddress, DestinationPaymentPointer = agent.PaymentPointer,
            RailDetails = "{}", CreatedAt = now, UpdatedAt = now,
        };
        var simulated = agent.PaymentPointer.StartsWith(relia.Value.SimulatedWalletPrefix, StringComparison.Ordinal);

        // 1. Verificación y monto. El monto queda fijo: lo que se acumule mientras el pago va en camino es
        // para la próxima.
        await unitOfWork.InTransactionAsync(async tx =>
        {
            var balance = await agents.GetBalanceForUpdateAsync(agentId, tx);
            await EnsureCanSettleAsync(agentId, balance, tx);
            settlement.Amount = balance.AccruedCommission;
            payment.Amount = settlement.Amount;
            settlement.PeriodStart = await PeriodStartAsync(agentId, today, tx);
            return true;
        }, ct);

        // 2. El riel, sin transacción abierta.
        var forcedFailure = demo.TryConsumeFailure();
        RailPayment? railPayment = null;
        if (simulated)
            payment.RailReference = "simulated";
        else if (!forcedFailure)
        {
            railPayment = await rail.CreateOrUnavailableAsync(new CreateRailPayment(payment.Id, RailDirection.Outgoing,
                agent.PaymentPointer, settlement.Amount, Description: $"Pago de comisión · {agent.Name}"), log, ct);
            OperationTracker.Record(payment, railPayment, now);
            PaymentLog.RailResponse(payment, "created", railPayment, now);
        }

        // 3. Guardar, verificando otra vez (la comisión solo crece, pero otra liquidación pudo adelantarse).
        try
        {
            await unitOfWork.InTransactionAsync(async tx =>
            {
                var balance = await agents.GetBalanceForUpdateAsync(agentId, tx);
                await EnsureCanSettleAsync(agentId, balance, tx);
                if (balance.AccruedCommission < settlement.Amount)
                    throw ApiErrors.Conflict("settlement_in_progress", "La comisión cambió mientras se pagaba");
                settlements.Add(settlement);
                payments.Add(payment);
                return true;
            }, ct);
        }
        catch (Exception e) when (railPayment is not null)
        {
            // Un envío ya en camino no se cancela: queda en el log para conciliar; la misma clave lo recupera.
            log.LogCritical(e,
                "El riel aceptó el pago de comisión {PaymentId} ({RailPaymentId}) pero la base no lo guardó",
                payment.Id, railPayment.Id);
            throw;
        }

        var created = (await settlements.GetTrackedAsync(settlement.Id, ct))!;
        if (forcedFailure)
            await tracker.VoidAsync(created, payment, "Fallo forzado desde el Modo demo", Db.EventSource.Simulator, ct);
        else if (simulated)
            await tracker.PayAsync(created, payment, Db.EventSource.Simulator, ct);
        return await ToDtoAsync(settlement.Id, ct);
    }

    public async Task<SettlementPageDto> ListAsync(SettlementQuery query, CancellationToken ct = default)
    {
        DateTime? before = null;
        if (query.Cursor is not null)
            before = long.TryParse(query.Cursor, out var ticks) && ticks > 0
                ? new DateTime(ticks, DateTimeKind.Utc)
                : throw ApiErrors.Validation("cursor", "Cursor inválido");

        // El agent solo ve las suyas: el filtro agentId se ignora.
        var agentId = currentUser.Role == Role.Agent ? currentUser.AgentId : query.AgentId;
        var items = await settlements.ListAsync(new SettlementFilter(
            query.From is { } from ? Clock.StartOfBogotaDayUtc(from) : null,
            query.To is { } to ? Clock.StartOfBogotaDayUtc(to.AddDays(1)) : null,
            agentId, query.Status?.ToDb(), before, query.Limit), ct);

        return new SettlementPageDto(items.Select(ToDto).ToList(),
            items.Count == query.Limit ? items[^1].CreatedAt.Ticks.ToString() : null);
    }

    public async Task<SettlementDto> GetAsync(Guid settlementId, CancellationToken ct = default)
    {
        var settlement = await settlements.GetTrackedAsync(settlementId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanRead(settlement.AgentId);
        await tracker.RefreshAsync(settlement, ct);
        return await ToDtoAsync(settlementId, ct);
    }

    private async Task EnsureCanSettleAsync(Guid agentId, Db.AgentBalance balance, CancellationToken ct)
    {
        if (await settlements.HasPendingAsync(agentId, ct))
            throw ApiErrors.Conflict("settlement_in_progress", "Ya hay un pago de comisión en camino");
        if (balance.AccruedCommission <= 0)
            throw ApiErrors.Unprocessable("no_commission", "No hay comisión por pagar");
    }

    /// <summary>Desde el día siguiente a la última liquidación pagada, o desde la primera operación, hasta hoy.</summary>
    private async Task<DateOnly> PeriodStartAsync(Guid agentId, DateOnly today, CancellationToken ct)
    {
        DateOnly start;
        if (await settlements.GetLastPaidAsync(agentId, ct) is { } last)
            start = last.PeriodEnd.AddDays(1);
        else if (await operations.GetFirstOperationDateAsync(agentId, ct) is { } first)
            start = Clock.BogotaDate(first);
        else
            start = today;
        return start > today ? today : start;
    }

    private async Task<SettlementDto> ToDtoAsync(Guid id, CancellationToken ct) =>
        ToDto((await settlements.GetByIdAsync(id, ct))!);

    private static SettlementDto ToDto(Db.Settlement s) => new(
        s.Id, s.Reference(), s.AgentId, s.Agent.Name, s.DestinationPaymentPointer, s.Amount, s.PeriodStart,
        s.PeriodEnd, s.Status.ToApi(), s.VoidReason, Clock.Utc(s.CreatedAt), Clock.Utc(s.PaidAt),
        new SettlementAdminDto(s.CreatedByAdmin.Id, s.CreatedByAdmin.Name));
}
