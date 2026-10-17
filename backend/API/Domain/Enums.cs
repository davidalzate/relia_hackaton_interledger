using NpgsqlTypes;

namespace Relia.Api.Domain;

// Enums de PostgreSQL de backend/db/db_ddl.sql. PgName da la etiqueta en la base.

public enum OperationKind
{
    [PgName("deposit")] Deposit,
    [PgName("withdrawal")] Withdrawal,
    [PgName("float_load")] QuotaTopUp,
}

public enum TopUpMethodKind
{
    [PgName("bre_b")] BreB,
    [PgName("wallet")] Wallet,
}

public enum OperationState
{
    [PgName("pending")] Pending,
    [PgName("completed")] Completed,
    [PgName("failed")] Failed,
    [PgName("expired")] Expired,
    [PgName("cancelled")] Cancelled,
}

public enum SettlementState
{
    [PgName("pending")] Pending,
    [PgName("paid")] Paid,
    [PgName("voided")] Voided,
}

public enum PaymentRail
{
    [PgName("open_payments")] OpenPayments,
    [PgName("bre_b_simulated")] BreBSimulated,
}

public enum PaymentDirection
{
    [PgName("incoming")] Incoming,
    [PgName("outgoing")] Outgoing,
}

public enum MessageChannel
{
    [PgName("sms")] Sms,
    [PgName("whatsapp")] WhatsApp,
}

public enum MessageKind
{
    [PgName("otp")] Otp,
    [PgName("wallet_invitation")] WalletInvitation,
    [PgName("deposit_notice")] DepositNotice,
    [PgName("payment_request")] PaymentRequest,
    [PgName("receipt")] Receipt,
    [PgName("commission_notice")] CommissionNotice,
}

public enum LedgerEntryKind
{
    [PgName("deposit")] Deposit,
    [PgName("withdrawal")] Withdrawal,
    [PgName("float_load")] QuotaTopUp,
    [PgName("settlement")] Settlement,
    [PgName("cash_adjustment")] CashAdjustment,
}

public enum OtpPurpose
{
    [PgName("wallet_linking")] KeyLink,
    [PgName("receipt_lookup")] ReceiptLookup,
}

public enum ReceiptChannel
{
    [PgName("screen")] Screen,
    [PgName("pdf")] Pdf,
    [PgName("print")] Print,
    [PgName("whatsapp")] WhatsApp,
    [PgName("sms")] Sms,
}

public enum ReceiptRequester
{
    [PgName("agent")] Agent,
    [PgName("user")] Customer,
    [PgName("system")] System,
}

public enum EventOrigin
{
    [PgName("agent")] Agent,
    [PgName("system")] System,
    [PgName("rail")] Rail,
    [PgName("simulator")] Simulator,
}
