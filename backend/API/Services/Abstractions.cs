using Relia.Api.Contracts;

namespace Relia.Api.Services;

// Un servicio por grupo de rutas del corresponsal. Los errores se lanzan como ApiException con el
// code del contrato (ver Errors/ApiErrors.cs). Las reglas de rol y propiedad viven aquí, con ICurrentUser.

/// <summary>POST /auth/login y GET /me.</summary>
public interface IAuthService
{
    /// <summary>401 invalid_credentials si el correo o la clave no coinciden.</summary>
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<MeDto> GetMeAsync(CancellationToken ct = default);
}

/// <summary>GET /agents, GET /agents/{agentId} y GET /agents/{agentId}/history.</summary>
public interface IAgentService
{
    /// <summary>Panel admin: todos los corresponsales con métricas y los totales de la red. 403 al agente.</summary>
    Task<AgentListDto> ListAgentsAsync(CancellationToken ct = default);

    /// <summary>Saldos con reservas calculadas de las operaciones pendientes. 403 si es de otro corresponsal.</summary>
    Task<AgentDto> GetAgentAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Operaciones, ajustes y liquidaciones, la más reciente primero, paginado por cursor.</summary>
    Task<HistoryPageDto> GetHistoryAsync(Guid agentId, HistoryEntryKind? kind, OperationStatus? status,
        string? cursor, int limit, CancellationToken ct = default);
}

/// <summary>POST /agents/{agentId}/cash-adjustments.</summary>
public interface ICashAdjustmentService
{
    /// <summary>422 insufficient_cash (set por debajo de lo reservado), 422 no_change, 403 al admin.</summary>
    Task<CashAdjustmentDto> CreateAsync(Guid agentId, string idempotencyKey, CreateCashAdjustmentRequest request,
        CancellationToken ct = default);
}

/// <summary>Desglose, depósitos, retiros, cargas de cupo y acciones sobre una operación.</summary>
public interface IOperationService
{
    /// <summary>Siempre 200; las reglas incumplidas van en violations.</summary>
    Task<OperationPreviewDto> PreviewAsync(Guid agentId, OperationPreviewRequest request,
        CancellationToken ct = default);

    /// <summary>202 pending. Reserva cupo y paga netAmount al usuario por el riel (outgoing).</summary>
    Task<OperationDto> CreateDepositAsync(Guid agentId, string idempotencyKey, CreateDepositRequest request,
        CancellationToken ct = default);

    /// <summary>202 pending con approvalUrl. Reserva efectivo y cobra chargedAmount al usuario (incoming).</summary>
    Task<OperationDto> CreateWithdrawalAsync(Guid agentId, string idempotencyKey, CreateWithdrawalRequest request,
        CancellationToken ct = default);

    /// <summary>202 pending. breB: qrPayload simulado (10 min). wallet: approvalUrl (5 min).</summary>
    Task<OperationDto> CreateQuotaTopUpAsync(Guid agentId, string idempotencyKey, CreateQuotaTopUpRequest request,
        CancellationToken ct = default);

    /// <summary>Refresca el pago de riel (máx. 1 vez/s) y devuelve el estado actual.</summary>
    Task<OperationDto> GetAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Retiros y cargas pendientes antes de aprobar; si no, 409 operation_not_cancellable.</summary>
    Task<OperationDto> CancelAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>
    /// Modo demo: llegó el pago Bre-B simulado de una carga pendiente y vigente. Si no, 409
    /// operation_not_cancellable (el contrato no nombra un código para este caso).
    /// </summary>
    Task<OperationDto> SimulateBreBPaymentAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Solo retiros completed; idempotente; no cambia saldos.</summary>
    Task<OperationDto> ConfirmCashHandoverAsync(Guid operationId, CancellationToken ct = default);
}

/// <summary>GET /keys/{phone} y POST /keys/{phone}/invitations.</summary>
public interface IKeyService
{
    /// <summary>404 key_not_found si el celular no tiene billetera vinculada.</summary>
    Task<KeyLookupDto> LookupAsync(string phone, CancellationToken ct = default);

    /// <summary>Deja el WhatsApp simulado en la bandeja del celular.</summary>
    Task<KeyInvitationDto> InviteAsync(string phone, string idempotencyKey, CancellationToken ct = default);
}

/// <summary>POST /key-links y POST /key-links/{challengeId}/verify, sin login.</summary>
public interface IKeyLinkService
{
    /// <summary>
    /// Valida en el orden del contrato (409 key_already_linked, phone_reserved, wallet_already_linked;
    /// 422 wallet_not_found; 503 rail_unavailable) y deja el OTP por SMS simulado.
    /// </summary>
    Task<KeyLinkChallengeDto> StartAsync(CreateKeyLinkRequest request, CancellationToken ct = default);

    /// <summary>Crea la llave con el código correcto; 422 invalid_otp, otp_expired u otp_attempts_exceeded.</summary>
    Task<KeyLinkDto> VerifyAsync(Guid challengeId, VerifyKeyLinkRequest request, CancellationToken ct = default);
}

/// <summary>Recibos congelados de depósitos y retiros.</summary>
public interface IReceiptService
{
    /// <summary>Registra una emisión; isCopy desde la segunda. 409 receipt_not_available / operation_not_completed.</summary>
    Task<ReceiptDto> GetAsync(Guid operationId, ReceiptViewChannel channel, CancellationToken ct = default);

    Task<ReceiptDeliveryDto> SendAsync(Guid operationId, SendReceiptRequest request, CancellationToken ct = default);
}

/// <summary>GET /settlements y GET /settlements/{settlementId}. El agente solo ve las suyas.</summary>
public interface ISettlementService
{
    Task<SettlementPageDto> ListAsync(SettlementQuery query, CancellationToken ct = default);

    Task<SettlementDto> GetAsync(Guid settlementId, CancellationToken ct = default);
}
