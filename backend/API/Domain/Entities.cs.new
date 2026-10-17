using System.Text.Json;

namespace Relia.Api.Domain;

// Una clase por tabla de backend/db/db_ddl.sql. El mapeo a columnas vive en Persistence/ReliaDbContext.

/// <summary>Tabla fees: tarifa vigente por operación, versionada.</summary>
public class Fee
{
    public long Id { get; set; }
    public long Total { get; set; }
    public long ReliaShare { get; set; }
    public long AgentShare { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset? ValidTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tabla agents.</summary>
public class Agent
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string ManagerName { get; set; } = "";
    public string City { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string WalletAddress { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Active { get; set; }

    /// <summary>Solo demo: la billetera no existe en el riel; su liquidación se paga sin riel.</summary>
    public bool WalletSimulated { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public AgentBalance? Balance { get; set; }
}

/// <summary>Tabla admins.</summary>
public class Admin
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Active { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Tabla agent_balances: cupo, comisión y efectivo de un corresponsal. Una fila por corresponsal.</summary>
public class AgentBalance
{
    public Guid AgentId { get; set; }
    public long Quota { get; set; }
    public long AccruedCommission { get; set; }
    public long SettledCommissionTotal { get; set; }
    public long CashOnHand { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Tabla wallet_keys: celular vinculado a un payment pointer. Inmutable.</summary>
public class WalletKey
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = "";
    public string WalletAddress { get; set; } = "";
    public string WalletAddressUrl { get; set; } = "";
    public DateTimeOffset LinkedAt { get; set; }
}

/// <summary>Tabla otp_challenges.</summary>
public class OtpChallenge
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = "";
    public OtpPurpose Purpose { get; set; }
    public string? WalletAddress { get; set; }
    public string CodeHash { get; set; } = "";
    public short Attempts { get; set; }
    public short MaxAttempts { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tabla operations: depósito, retiro o carga de cupo.</summary>
public class Operation
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public string Reference { get; set; } = "";
    public Guid AgentId { get; set; }
    public OperationKind Kind { get; set; }
    public TopUpMethodKind? TopUpMethod { get; set; }
    public Guid? WalletKeyId { get; set; }
    public long RequestedAmount { get; set; }
    public bool RoundAmount { get; set; }
    public long? FeeId { get; set; }
    public long FeeTotal { get; set; }
    public long FeeRelia { get; set; }
    public long FeeAgent { get; set; }
    public long NetAmount { get; set; }
    public long ChargedAmount { get; set; }
    public OperationState State { get; set; }

    /// <summary>FailureCode del contrato (customer_rejected, payment_failed…) en failed, expired o cancelled.</summary>
    public string? FailureCode { get; set; }

    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Solo retiros completados: cuándo se confirmó la entrega del efectivo.</summary>
    public DateTimeOffset? CashHandedOverAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Agent? Agent { get; set; }
    public WalletKey? WalletKey { get; set; }
}

/// <summary>Tabla simulated_messages: bandeja del teléfono simulado.</summary>
public class SimulatedMessage
{
    public long Id { get; set; }
    public MessageChannel Channel { get; set; }
    public MessageKind Kind { get; set; }
    public string Recipient { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Link { get; set; }
    public Guid? OperationId { get; set; }
    public bool Read { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tabla settlements: pago de la comisión acumulada.</summary>
public class Settlement
{
    public Guid Id { get; set; }
    public long Number { get; set; }
    public string Reference { get; set; } = "";
    public Guid AgentId { get; set; }
    public Guid CreatedByAdminId { get; set; }
    public string DestinationWalletAddress { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public long Amount { get; set; }
    public SettlementState State { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Agent? Agent { get; set; }
    public Admin? CreatedByAdmin { get; set; }
}

/// <summary>Tabla payments: intento de pago por riel de una operación o liquidación.</summary>
public class Payment
{
    public Guid Id { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? SettlementId { get; set; }
    public PaymentRail Rail { get; set; }
    public PaymentDirection Direction { get; set; }
    public OperationState State { get; set; }
    public long Amount { get; set; }
    public string Currency { get; set; } = "COP";
    public string SourceWalletAddress { get; set; } = "";
    public string DestinationWalletAddress { get; set; } = "";
    public string? IncomingPaymentUrl { get; set; }
    public string? QuoteUrl { get; set; }
    public string? OutgoingPaymentUrl { get; set; }
    public string? ApprovalUrl { get; set; }
    public string? GrantContinueUri { get; set; }
    public byte[]? GrantContinueTokenEncrypted { get; set; }
    public string? RailReference { get; set; }

    /// <summary>Id del pago en el proxy de riel. Null si no pasó por el proxy.</summary>
    public Guid? RailPaymentId { get; set; }

    /// <summary>Último version del proxy ya procesado.</summary>
    public int? RailVersion { get; set; }

    public string? DebitValue { get; set; }
    public string? DebitAssetCode { get; set; }
    public short? DebitAssetScale { get; set; }
    public string? ReceivedValue { get; set; }
    public string? ReceivedAssetCode { get; set; }
    public short? ReceivedAssetScale { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public JsonDocument RailDetail { get; set; } = JsonDocument.Parse("{}");
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Tabla payment_events: bitácora de solo inserción de cada pago.</summary>
public class PaymentEvent
{
    public long Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Action { get; set; } = "";
    public OperationState? PreviousState { get; set; }
    public OperationState NewState { get; set; }
    public EventOrigin Origin { get; set; }
    public short? HttpStatus { get; set; }
    public JsonDocument? RailResponse { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tabla operation_events: bitácora de solo inserción de cada operación.</summary>
public class OperationEvent
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public OperationState? PreviousState { get; set; }
    public OperationState NewState { get; set; }
    public EventOrigin Origin { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tabla ledger_entries: libro de solo inserción de cupo, comisión y efectivo.</summary>
public class LedgerEntry
{
    public long Id { get; set; }
    public Guid AgentId { get; set; }
    public LedgerEntryKind Kind { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? SettlementId { get; set; }
    public Guid? PaymentId { get; set; }
    public long QuotaBefore { get; set; }
    public long QuotaChange { get; set; }
    public long QuotaAfter { get; set; }
    public long CommissionBefore { get; set; }
    public long CommissionChange { get; set; }
    public long CommissionAfter { get; set; }
    public long CashBefore { get; set; }
    public long CashChange { get; set; }
    public long CashAfter { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tabla receipts: contenido congelado del recibo de un depósito o retiro.</summary>
public class Receipt
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public string Code { get; set; } = "";
    public JsonDocument Data { get; set; } = JsonDocument.Parse("{}");
    public string ContentHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tabla receipt_issuances: cada vez que un recibo se muestra o se envía.</summary>
public class ReceiptIssue
{
    public long Id { get; set; }
    public Guid ReceiptId { get; set; }
    public ReceiptChannel Channel { get; set; }
    public ReceiptRequester RequestedBy { get; set; }
    public Guid? AgentId { get; set; }
    public string? CustomerPhone { get; set; }
    public bool IsCopy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Tabla idempotency_records: respuesta exitosa guardada por Idempotency-Key.</summary>
public class IdempotencyRecord
{
    public long Id { get; set; }

    /// <summary>Ruta + actor, para que la misma clave en otra ruta o de otro actor no choque.</summary>
    public string Scope { get; set; } = "";

    public string IdempotencyKey { get; set; } = "";

    /// <summary>SHA-256 en hex del cuerpo de la petición.</summary>
    public string RequestHash { get; set; } = "";

    public short ResponseStatus { get; set; }
    public JsonDocument ResponseBody { get; set; } = JsonDocument.Parse("{}");
    public string? ResourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
