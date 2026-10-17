using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Relia.Api.Contracts;

// DTOs de contracts/relia-api.openapi.yaml usados por las rutas del corresponsal.
// Dinero: long en pesos COP. Fechas: DateTimeOffset en UTC.

internal static class Patterns
{
    public const string Phone = @"^\+[1-9][0-9]{7,14}$";

    /// <summary>Payment pointer ($dominio/ruta).</summary>
    public const string WalletAddress = @"^\$[^\s/]+(/\S*)?$";

    /// <summary>Tope de cualquier monto de entrada ($1 billón): evita desbordar long al sumar tarifa o saldos.</summary>
    public const long MaxCop = 1_000_000_000_000;
}

// ---- Enums del API ----

public enum Role { Agent, Admin }

public enum CashMovingOperationType { Deposit, Withdrawal }

public enum OperationType { Deposit, Withdrawal, QuotaTopUp }

public enum OperationStatus { Pending, Completed, Failed, Expired, Cancelled }

public enum TopUpMethod { BreB, Wallet }

public enum CashAdjustmentMode { Add, Set }

public enum HistoryEntryKind { Deposit, Withdrawal, QuotaTopUp, CashAdjustment, Settlement }

public enum SettlementStatus { Pending, Paid, Voided }

public enum ReceiptViewChannel { Screen, Print, Pdf }

public enum ReceiptSendChannel { Sms, [JsonStringEnumMemberName("whatsapp")] WhatsApp }

public enum PaymentRailName { OpenPayments, BreBSimulated }

public enum FailureCode
{
    [JsonStringEnumMemberName("customer_rejected")] CustomerRejected,
    [JsonStringEnumMemberName("request_expired")] RequestExpired,
    [JsonStringEnumMemberName("request_cancelled")] RequestCancelled,
    [JsonStringEnumMemberName("payment_failed")] PaymentFailed,
    [JsonStringEnumMemberName("rail_unavailable")] RailUnavailable,
}

public enum PreviewViolationCode
{
    [JsonStringEnumMemberName("amount_too_low")] AmountTooLow,
    [JsonStringEnumMemberName("insufficient_quota")] InsufficientQuota,
    [JsonStringEnumMemberName("insufficient_cash")] InsufficientCash,
}

// ---- Auth ----

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record LoginResponse(string AccessToken, int ExpiresIn, Role Role, Guid? AgentId);

public record MeDto(Role Role, string Email, string Name, AgentDto? Agent);

// ---- Corresponsal ----

public record AgentBalancesDto(
    long Quota, long QuotaReserved, long CashOnHand, long CashReserved,
    long AccruedCommission, long SettledCommissionTotal);

public record AgentDto(
    Guid Id, string Name, string ManagerName, string City, string Address,
    string Phone, string WalletAddress, AgentBalancesDto Balances);

public record BalancePairDto(long Before, long After);

// ---- Panel admin ----

public record CountAmountDto(int Count, long Amount);

public record AgentSummaryDto(
    Guid Id, string Name, string ManagerName, string City, string WalletAddress, bool WalletSimulated,
    int Operations, CountAmountDto Deposits, CountAmountDto Withdrawals, long TotalMoved, long Quota,
    long CashOnHand, long AccruedCommission, long SettledCommissionTotal, long CommissionPaidToday);

public record NetworkTotalsDto(
    CountAmountDto TotalMoved, CountAmountDto Deposits, CountAmountDto Withdrawals,
    long CommissionPayable, long CommissionPaidToday);

public record AgentListDto(IReadOnlyList<AgentSummaryDto> Items, NetworkTotalsDto Totals);

public record BalanceChangeDto(BalancePairDto Quota, BalancePairDto AccruedCommission, BalancePairDto CashOnHand);

// ---- Historial ----

public record HistoryEntryDto(
    string Id, HistoryEntryKind Kind, string? Reference, DateTimeOffset OccurredAt, OperationStatus Status,
    long Amount, BalanceChangeDto Balances, string? Reason, FailureCode? FailureCode,
    Guid? OperationId, Guid? SettlementId);

public record HistoryPageDto(IReadOnlyList<HistoryEntryDto> Items, string? NextCursor);

// ---- Efectivo en caja ----

// Nullable + [Required]: con tipos de valor, un campo ausente llegaría como 0 o como el primer valor del enum.
public record CreateCashAdjustmentRequest(
    [Required] CashAdjustmentMode? Mode,
    [Required, Range(0, Patterns.MaxCop)] long? Amount,
    [MaxLength(200)] string? Reason);

public record CashAdjustmentDto(
    string Id, Guid AgentId, CashAdjustmentMode Mode, long Variation, string Reason,
    BalancePairDto CashOnHand, DateTimeOffset CreatedAt);

// ---- Desglose ----

public record OperationPreviewRequest(
    [Required] CashMovingOperationType? Type,
    [Required, Range(1, Patterns.MaxCop)] long? RequestedAmount,
    [Required] bool? RoundAmount);

public record FeeDto(long Total, long Relia, long Agent);

// shortfall es Cop (entero, no nullable): sin valor, se omite.
public record PreviewViolationDto(
    PreviewViolationCode Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Shortfall);

public record OperationPreviewDto(
    CashMovingOperationType Type, long RequestedAmount, bool RoundAmount, FeeDto Fee,
    long NetAmount, long ChargedAmount, long QuotaAfter, long CashOnHandAfter, long AccruedCommissionAfter,
    IReadOnlyList<PreviewViolationDto> Violations);

// ---- Llaves ----

public record KeyLookupDto(string Phone, string WalletAddressMasked);

public record KeyInvitationDto(string Phone, string WaLink, string Message, DateTimeOffset SentAt);

// ---- Vinculación pública con OTP ----

public record CreateKeyLinkRequest(
    [Required, RegularExpression(Patterns.Phone)] string Phone,
    [Required, RegularExpression(Patterns.WalletAddress)] string WalletAddress);

public record KeyLinkChallengeDto(Guid ChallengeId, string Phone, DateTimeOffset ExpiresAt, int MaxAttempts);

public record VerifyKeyLinkRequest([Required, RegularExpression("^[0-9]{6}$")] string Code);

/// <summary>walletAddress completo: es el eco de lo que escribió el usuario.</summary>
public record KeyLinkDto(string Phone, string WalletAddress, DateTimeOffset LinkedAt);

// ---- Operaciones ----

public record CreateDepositRequest(
    [Required, RegularExpression(Patterns.Phone)] string Phone,
    [Required, Range(1001, Patterns.MaxCop)] long? RequestedAmount,
    [Required] bool? RoundAmount,
    // const true en el contrato: crear el depósito es confirmar que se recibió el efectivo.
    [Required, Range(typeof(bool), "true", "true", ErrorMessage = "Debe confirmar que recibió el efectivo.")]
    bool? CashReceivedConfirmed);

public record CreateWithdrawalRequest(
    [Required, RegularExpression(Patterns.Phone)] string Phone,
    [Required, Range(1001, Patterns.MaxCop)] long? RequestedAmount,
    [Required] bool? RoundAmount);

public record CreateQuotaTopUpRequest(
    [Required] TopUpMethod? Method,
    [Required, Range(1, Patterns.MaxCop)] long? RequestedAmount);

public record OperationCustomerDto(string PhoneMasked, string WalletAddressMasked);

public record OperationDto(
    Guid Id, string Reference, Guid AgentId, OperationType Type, OperationStatus Status,
    FailureCode? FailureCode, string? FailureDetail,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, DateTimeOffset? ExpiresAt,
    long RequestedAmount, bool RoundAmount, FeeDto Fee, long NetAmount, long ChargedAmount,
    OperationCustomerDto? Customer, TopUpMethod? TopUpMethod, string? ApprovalUrl, string? QrPayload,
    DateTimeOffset? CashHandedOverAt, BalanceChangeDto Balances);

// ---- Recibos ----

public record ReceiptMerchantDto(string Name, string Address, string Phone);

public record ReceiptAmountsDto(long RequestedAmount, bool RoundAmount, long Fee, long NetAmount, long ChargedAmount);

public record ReceiptPaymentDto(PaymentRailName Rail, string Reference);

public record ReceiptDataDto(
    string Reference, CashMovingOperationType Type, DateTimeOffset Date, string Currency,
    ReceiptMerchantDto Merchant, OperationCustomerDto Customer, ReceiptAmountsDto Amounts, ReceiptPaymentDto Payment);

public record ReceiptDto(
    string Code, Guid OperationId, string ContentHash, bool IsCopy, DateTimeOffset IssuedAt, ReceiptDataDto Data);

public record SendReceiptRequest([Required] ReceiptSendChannel? Channel);

public record ReceiptDeliveryDto(ReceiptSendChannel Channel, string SentTo, bool IsCopy, DateTimeOffset SentAt);

// ---- Liquidaciones ----

public record SettlementAdminDto(Guid Id, string Name);

public record SettlementDto(
    Guid Id, string Reference, Guid AgentId, string AgentName, string WalletAddress, long Amount,
    DateOnly PeriodFrom, DateOnly PeriodTo, SettlementStatus Status, string? VoidReason,
    DateTimeOffset CreatedAt, DateTimeOffset? PaidAt, SettlementAdminDto CreatedByAdmin);

public record SettlementPageDto(IReadOnlyList<SettlementDto> Items, string? NextCursor);

public record SettlementQuery(
    DateOnly? From, DateOnly? To, Guid? AgentId, SettlementStatus? Status, string? Cursor, int Limit);

// ---- Modo demo: "Mi billetera" del teléfono simulado ----

public enum DemoPaymentRequestType { Withdrawal, QuotaTopUp }

public record DemoPaymentRequestDto(
    Guid OperationId, string Reference, DemoPaymentRequestType Type, string AgentName, long ChargedAmount,
    DateTimeOffset ExpiresAt, string ApprovalUrl);

public record DemoPaymentRequestListDto(IReadOnlyList<DemoPaymentRequestDto> Items);

// ---- Modo demo: fallos forzados ----

public record DemoFaultsDto(bool FailNextPayment);

public record SetDemoFaultsRequest([Required] bool? FailNextPayment);

// ---- Modo demo: bandeja del teléfono simulado ----

public enum DemoMessageChannel { Sms, [JsonStringEnumMemberName("whatsapp")] WhatsApp }

/// <summary>Nombres del contrato; la base usa otros (wallet_invitation, deposit_notice…), ver SimulatedInbox.</summary>
public enum DemoMessageType
{
    [JsonStringEnumMemberName("otp")] Otp,
    [JsonStringEnumMemberName("invitacion_wallet")] WalletInvitation,
    [JsonStringEnumMemberName("aviso_deposito")] DepositNotice,
    [JsonStringEnumMemberName("solicitud_pago")] PaymentRequest,
    [JsonStringEnumMemberName("comprobante")] Receipt,
    [JsonStringEnumMemberName("aviso_comision")] CommissionNotice,
}

public record DemoMessageDto(
    string Id, string Phone, DemoMessageChannel Channel, DemoMessageType Type, string Content, string? Link,
    Guid? OperationId, bool Read, DateTimeOffset CreatedAt);

public record DemoMessagePageDto(IReadOnlyList<DemoMessageDto> Items, int UnreadCount, string? NextCursor);

/// <summary>Sin celular, marca todos.</summary>
public record MarkDemoMessagesReadRequest([RegularExpression(Patterns.Phone)] string? Phone);
