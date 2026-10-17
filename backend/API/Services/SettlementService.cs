using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Relia.Api.Auth;
using Relia.Api.Configuration;
using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Rail;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Pago de la comisión acumulada de un corresponsal a su billetera, y su lectura. La idempotencia de la creación
/// la resuelve el filtro [Idempotent] del controlador, que abre la transacción: un 4xx o un 503 deshacen todo.
/// </summary>
public class SettlementService(
    ISettlementRepository settlements, IAgentRepository agents, IOperationRepository operations,
    IPaymentRepository payments, ILedger ledger, IRailClient rail, ISettlementSync sync, DemoFaults faults,
    IUnitOfWork unitOfWork, ICurrentUser currentUser, IOptions<ReliaOptions> options, TimeProvider time,
    ILogger<SettlementService> logger)
    : ISettlementService
{
    /// <summary>
    /// Liquida toda la comisión acumulada, que queda fija: lo que se acumule mientras el pago va en camino queda para
    /// la próxima. El saldo queda bloqueado (FOR UPDATE) mientras se decide, así dos liquidaciones simultáneas del
    /// mismo corresponsal no pasan las dos el chequeo de "ya hay una pendiente".
    /// </summary>
    public async Task<SettlementDto> CreateAsync(Guid agentId, string idempotencyKey, CancellationToken ct = default)
    {
        currentUser.EnsureAdmin();
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();

        var settlementId = await unitOfWork.InTransactionAsync(async tx =>
        {
            var balance = await ledger.LockAsync(agentId, tx);
            if (await settlements.HasPendingAsync(agentId, tx)) throw ApiErrors.SettlementInProgress();
            var amount = balance.AccruedCommission;
            if (amount <= 0) throw ApiErrors.NoCommission();

            var now = time.GetUtcNow();
            var today = Bogota.DateOf(now);
            var settlement = new Settlement
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                CreatedByAdminId = currentUser.UserId,
                DestinationWalletAddress = agent.WalletAddress,
                PeriodFrom = await PeriodStartAsync(agentId, today, tx),
                PeriodTo = today,
                Amount = amount,
                State = SettlementState.Pending,
                CreatedAt = now,
            };
            settlements.Add(settlement);

            // En demo, la billetera simulada no existe en el riel: el pago se marca hecho sin llamar al proxy.
            var simulated = options.Value.DemoMode && agent.WalletSimulated;
            var payment = new Payment
            {
                Id = RailPayments.PaymentId(agentId, "settlement", idempotencyKey),
                SettlementId = settlement.Id,
                Rail = simulated ? PaymentRail.BreBSimulated : PaymentRail.OpenPayments,
                Direction = PaymentDirection.Outgoing,
                State = OperationState.Pending,
                Amount = amount,
                SourceWalletAddress = options.Value.WalletAddress,
                DestinationWalletAddress = agent.WalletAddress,
                CreatedAt = now,
            };
            payments.Add(payment);
            // Se guarda antes de llamar al riel: si la base rechaza algo, no se llega a pedir el pago.
            await unitOfWork.SaveChangesAsync(tx);

            // "El próximo pago falla" del Modo demo: voided sin llamar al riel, y el interruptor se apaga.
            if (faults.TryFailPayment(payments, payment, now))
            {
                await sync.VoidAsync(settlement, payment, OperationState.Failed, DemoFaults.ForcedFailureDetail, tx);
                return settlement.Id;
            }

            if (simulated)
            {
                // La referencia LQ-xxxxxx la asigna la base al insertar.
                payment.RailReference = $"SIM-{settlement.Reference}";
                payment.RailDetail = JsonSerializer.SerializeToDocument(new Dictionary<string, object?> { ["simulated"] = true });
                payments.AddEvent(new PaymentEvent
                {
                    PaymentId = payment.Id, Action = "simulate", PreviousState = payment.State,
                    NewState = OperationState.Completed, Origin = EventOrigin.System,
                    Detail = "Billetera simulada: liquidación pagada sin riel", CreatedAt = now,
                });
                await sync.PayAsync(settlement, payment, now, tx);
                return settlement.Id;
            }

            var reading = await rail.CreateOrThrowAsync(payment, new RailPaymentRequest(
                RailDirection.Outgoing, agent.WalletAddress, RailAmount.FromCop(amount),
                Description: RailPayments.Description($"Comisión de {agent.Name} en Relia")),
                logger, tx);
            await sync.ApplyAsync(settlement, payment, reading, "create", tx);
            return settlement.Id;
        }, ct);

        return ToDto(await settlements.GetByIdAsync(settlementId, ct) ?? throw ApiErrors.NotFound());
    }

    /// <summary>El agente solo ve las suyas: su agentId reemplaza el filtro. from y to son días en Bogotá, inclusive.</summary>
    public async Task<SettlementPageDto> ListAsync(SettlementQuery query, CancellationToken ct = default)
    {
        var agentId = currentUser.Role == Role.Agent ? currentUser.AgentId : query.AgentId;
        // Una fila de más para saber si hay página siguiente.
        var filter = new SettlementFilter(
            query.From is { } from ? Bogota.StartOfDay(from) : null,
            query.To is { } to ? Bogota.StartOfDay(to.AddDays(1)) : null,
            agentId,
            query.Status switch
            {
                SettlementStatus.Pending => SettlementState.Pending,
                SettlementStatus.Paid => SettlementState.Paid,
                SettlementStatus.Voided => SettlementState.Voided,
                _ => null,
            },
            query.Cursor is null ? null : DecodeCursor(query.Cursor),
            query.Limit + 1);
        var rows = await settlements.ListAsync(filter, ct);

        var page = rows.Take(query.Limit).ToList();
        var next = rows.Count > query.Limit ? EncodeCursor(page[^1].CreatedAt) : null;
        return new SettlementPageDto(page.Select(ToDto).ToList(), next);
    }

    /// <summary>404 si no existe; 403 si es de otro corresponsal. Si está pendiente, refresca antes su pago.</summary>
    public async Task<SettlementDto> GetAsync(Guid settlementId, CancellationToken ct = default)
    {
        var settlement = await settlements.GetByIdAsync(settlementId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanReadAgent(settlement.AgentId);
        await sync.RefreshAsync(settlement, ct);
        return ToDto(settlement);
    }

    /// <summary>
    /// Desde el día siguiente al período de la última liquidación pagada; si no hay, desde el día de la primera
    /// operación completada; si tampoco, hoy. Nunca después de hoy (ck_settlement_period).
    /// </summary>
    private async Task<DateOnly> PeriodStartAsync(Guid agentId, DateOnly today, CancellationToken ct)
    {
        DateOnly start;
        if (await settlements.GetLastPaidAsync(agentId, ct) is { } lastPaid)
            start = lastPaid.PeriodTo.AddDays(1);
        else if (await operations.GetFirstCompletedAtAsync(agentId, ct) is { } first)
            start = Bogota.DateOf(first);
        else
            start = today;
        return start > today ? today : start;
    }

    /// <summary>Requiere la liquidación con Agent y CreatedByAdmin cargados.</summary>
    private static SettlementDto ToDto(Settlement s) => new(
        s.Id, s.Reference, s.AgentId, s.Agent?.Name ?? "", s.DestinationWalletAddress, s.Amount,
        s.PeriodFrom, s.PeriodTo,
        s.State switch
        {
            SettlementState.Pending => SettlementStatus.Pending,
            SettlementState.Paid => SettlementStatus.Paid,
            _ => SettlementStatus.Voided,
        },
        s.VoidReason, s.CreatedAt, s.PaidAt,
        new SettlementAdminDto(s.CreatedByAdminId, s.CreatedByAdmin?.Name ?? ""));

    /// <summary>Cursor = created_at (ticks UTC) de la última liquidación de la página.</summary>
    private static string EncodeCursor(DateTimeOffset createdAt) =>
        createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset DecodeCursor(string cursor)
    {
        if (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
            throw ApiErrors.Validation("cursor", "Cursor inválido.");
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
