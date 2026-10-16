using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

// Los stubs aún no usan sus dependencias; se quita al implementar el último servicio.
#pragma warning disable CS9113

// Implementaciones en desarrollo: reciben ya sus repositorios y responden 501 "En desarrollo".
// Cada una se implementa en el ticket indicado de docs/wayfinder/relia-api/issues/.

/// <summary>Ticket 02.</summary>
public class AuthService(IAgentRepository agents, IAdminRepository admins, ICurrentUser currentUser)
    : IAuthService
{
    public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("login");

    public Task<MeDto> GetMeAsync(CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("me");
}

/// <summary>Ticket 04.</summary>
public class AgentService(
    IAgentRepository agents, IOperationRepository operations, ILedgerRepository ledger,
    ISettlementRepository settlements, ICurrentUser currentUser) : IAgentService
{
    public Task<AgentDto> GetAgentAsync(Guid agentId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getAgent");

    public Task<HistoryPageDto> GetHistoryAsync(Guid agentId, HistoryEntryKind? kind, OperationStatus? status,
        string? cursor, int limit, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("listAgentHistory");
}

/// <summary>Ticket 05.</summary>
public class CashAdjustmentService(
    IAgentRepository agents, IOperationRepository operations, ILedgerRepository ledger, IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICashAdjustmentService
{
    public Task<CashAdjustmentDto> CreateAsync(Guid agentId, string idempotencyKey,
        CreateCashAdjustmentRequest request, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("createCashAdjustment");
}

/// <summary>Tickets 06 (desglose), 09 (depósito), 11 (retiro) y 12 (carga de cupo).</summary>
public class OperationService(
    IAgentRepository agents, IFeeRepository fees, IWalletKeyRepository keys, IOperationRepository operations,
    IPaymentRepository payments, ILedgerRepository ledger, ISimulatedMessageRepository messages,
    IUnitOfWork unitOfWork, ICurrentUser currentUser) : IOperationService
{
    public Task<OperationPreviewDto> PreviewAsync(Guid agentId, OperationPreviewRequest request,
        CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("previewOperation");

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

/// <summary>Ticket 07.</summary>
public class KeyService(IWalletKeyRepository keys, ISimulatedMessageRepository messages, IUnitOfWork unitOfWork)
    : IKeyService
{
    public Task<KeyLookupDto> LookupAsync(string phone, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getKey");

    public Task<KeyInvitationDto> InviteAsync(string phone, string idempotencyKey, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("createKeyInvitation");
}

/// <summary>Ticket 10.</summary>
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

/// <summary>Ticket 15 (lectura).</summary>
public class SettlementService(ISettlementRepository settlements, ICurrentUser currentUser) : ISettlementService
{
    public Task<SettlementPageDto> ListAsync(SettlementQuery query, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("listSettlements");

    public Task<SettlementDto> GetAsync(Guid settlementId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("getSettlement");
}
