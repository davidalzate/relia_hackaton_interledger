using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services;

/// <summary>Ticket 05: sumar efectivo o fijar el total en caja. No toca cupo ni comisión.</summary>
public class CashAdjustmentService(
    IAgentRepository agents, IOperationRepository operations, BalanceLedger balanceLedger, IUnitOfWork unitOfWork,
    IdempotencyStore idempotency, ICurrentUser currentUser) : ICashAdjustmentService
{
    public Task<CashAdjustmentDto> CreateAsync(Guid agentId, string idempotencyKey,
        CreateCashAdjustmentRequest request, CancellationToken ct = default)
    {
        currentUser.EnsureCanOperate(agentId);
        return idempotency.RunAsync($"{agentId}|cash-adjustments", idempotencyKey, request,
            () => CreateAsync(agentId, request, ct));
    }

    private async Task<CashAdjustmentDto> CreateAsync(Guid agentId, CreateCashAdjustmentRequest request,
        CancellationToken ct)
    {
        if (request.Mode == CashAdjustmentMode.Add && request.Amount <= 0)
            throw ApiErrors.Validation("amount", "Con add, el monto debe ser mayor a 0");

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? request.Mode == CashAdjustmentMode.Add ? "Asignación de efectivo" : "Ajuste de efectivo"
            : request.Reason.Trim();

        var entry = await unitOfWork.InTransactionAsync(async tx =>
        {
            var balance = await agents.GetBalanceForUpdateAsync(agentId, tx);
            var variation = request.Mode == CashAdjustmentMode.Add ? request.Amount : request.Amount - balance.ExpectedCash;
            if (variation == 0) throw ApiErrors.Unprocessable("no_change", "El efectivo no cambia");

            var reserved = (await operations.GetReservationsAsync(agentId, tx)).CashReserved;
            var newTotal = balance.ExpectedCash + variation;
            if (newTotal < reserved)
                throw ApiErrors.Shortfall("insufficient_cash", "Efectivo por debajo del reservado", reserved - newTotal);

            return await balanceLedger.ApplyAsync(new LedgerMove(agentId, Db.LedgerEntryType.CashAdjustment,
                0, 0, variation, Reason: reason), tx);
        }, ct);

        return new CashAdjustmentDto($"adj-{entry.Id}", agentId, request.Mode, entry.CashChange, reason,
            new BalancePairDto(entry.CashBefore, entry.CashAfter), Clock.Utc(entry.CreatedAt));
    }
}
