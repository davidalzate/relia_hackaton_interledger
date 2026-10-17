using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// POST /agents/{agentId}/cash-adjustments. No toca cupo ni comisión. La idempotencia la resuelve el filtro
/// [Idempotent] del controlador, así que la clave no se usa aquí.
/// </summary>
public class CashAdjustmentService(
    IOperationRepository operations, ILedger ledger, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    : ICashAdjustmentService
{
    private const string DefaultAddReason = "Asignación de efectivo";
    private const string DefaultSetReason = "Ajuste de efectivo";

    public async Task<CashAdjustmentDto> CreateAsync(Guid agentId, string idempotencyKey,
        CreateCashAdjustmentRequest request, CancellationToken ct = default)
    {
        currentUser.EnsureCanOperateAgent(agentId);
        // [Required] en el DTO garantiza ambos valores.
        var mode = request.Mode!.Value;
        var amount = request.Amount!.Value;
        var isAdd = mode == CashAdjustmentMode.Add;
        if (isAdd && amount <= 0)
            throw ApiErrors.Validation("amount", "Con mode add, el monto debe ser mayor a 0.");

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? isAdd ? DefaultAddReason : DefaultSetReason
            : request.Reason.Trim();

        var entry = await unitOfWork.InTransactionAsync(async tx =>
        {
            var balance = await ledger.LockAsync(agentId, tx);
            var newCash = isAdd ? balance.CashOnHand + amount : amount;
            var variation = newCash - balance.CashOnHand;
            if (variation == 0)
                throw new ApiException(StatusCodes.Status422UnprocessableEntity, "no_change", "El ajuste no cambia el efectivo");

            // El efectivo comprometido por retiros pendientes no se puede bajar con un ajuste.
            var reserved = (await operations.GetReservationsAsync(agentId, tx)).CashReserved;
            if (newCash < reserved) throw ApiErrors.InsufficientCash(reserved - newCash);

            return ledger.Post(balance, new LedgerPosting(LedgerEntryKind.CashAdjustment, CashChange: variation, Reason: reason));
        }, ct);

        return new CashAdjustmentDto(
            $"cash-{entry.Id}", agentId, mode, entry.CashChange, reason,
            new BalancePairDto(entry.CashBefore, entry.CashAfter), entry.CreatedAt);
    }
}
