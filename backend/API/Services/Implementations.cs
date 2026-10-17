using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

// Los stubs aún no usan sus dependencias; se quita al implementar el último servicio.
#pragma warning disable CS9113

// Implementaciones en desarrollo: reciben ya sus repositorios y responden 501 "En desarrollo".
// Cada una se implementa en el ticket indicado de docs/issues/backend-relia-api/.

/// <summary>Tickets 04 (desglose), 07 (depósito), 09 (retiro) y 10 (carga de cupo).</summary>
public class OperationService(
    IAgentRepository agents, IFeeRepository fees, IWalletKeyRepository keys, IOperationRepository operations,
    IPaymentRepository payments, ILedgerRepository ledger, ISimulatedMessageRepository messages,
    IUnitOfWork unitOfWork, ICurrentUser currentUser) : IOperationService
{
    /// <summary>Siempre 200: lo que impide crear la operación va en violations.</summary>
    public async Task<OperationPreviewDto> PreviewAsync(Guid agentId, OperationPreviewRequest request,
        CancellationToken ct = default)
    {
        currentUser.EnsureCanOperateAgent(agentId);
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        var balance = agent.Balance
                      ?? throw new InvalidOperationException($"El corresponsal {agent.Id} no tiene agent_balances");
        var fee = await fees.GetCurrentAsync(ct);
        var reserved = await operations.GetReservationsAsync(agentId, ct);
        // [Required] en el DTO garantiza los tres valores.
        return OperationRules.Preview(
            request.Type!.Value, request.RequestedAmount!.Value, request.RoundAmount!.Value, fee, balance, reserved);
    }

    public Task<OperationDto> CreateDepositAsync(Guid agentId, string idempotencyKey, CreateDepositRequest request,
        CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("createDeposit");

    public Task<OperationDto> CreateWithdrawalAsync(Guid agentId, string idempotencyKey,
        CreateWithdrawalRequest request, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("createWithdrawal");

    public Task<OperationDto> CreateQuotaTopUpAsync(Guid agentId, string idempotencyKey,
        CreateQuotaTopUpRequest request, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("createQuotaTopUp");

    public Task<OperationDto> GetAsync(Guid operationId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getOperation");

    public Task<OperationDto> CancelAsync(Guid operationId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("cancelOperation");

    public Task<OperationDto> ConfirmCashHandoverAsync(Guid operationId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("confirmCashHandover");
}

/// <summary>Ticket 05.</summary>
public class KeyService(IWalletKeyRepository keys, ISimulatedMessageRepository messages, IUnitOfWork unitOfWork)
    : IKeyService
{
    public Task<KeyLookupDto> LookupAsync(string phone, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getKey");

    public Task<KeyInvitationDto> InviteAsync(string phone, string idempotencyKey, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("createKeyInvitation");
}

/// <summary>Ticket 08.</summary>
public class ReceiptService(
    IOperationRepository operations, IReceiptRepository receipts, ISimulatedMessageRepository messages,
    IUnitOfWork unitOfWork, ICurrentUser currentUser) : IReceiptService
{
    public Task<ReceiptDto> GetAsync(Guid operationId, ReceiptViewChannel channel, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getReceipt");

    public Task<ReceiptDeliveryDto> SendAsync(Guid operationId, SendReceiptRequest request,
        CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("sendReceipt");
}

/// <summary>Ticket 13.</summary>
public class SettlementService(ISettlementRepository settlements, ICurrentUser currentUser) : ISettlementService
{
    public Task<SettlementPageDto> ListAsync(SettlementQuery query, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("listSettlements");

    public Task<SettlementDto> GetAsync(Guid settlementId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getSettlement");
}
