namespace Relia.Api.Entities;

// Enums de PostgreSQL. El scaffold no los genera: este archivo es manual y sobrevive a un nuevo scaffold.
// Npgsql traduce los nombres de PascalCase a snake_case (FloatLoad → float_load), así que deben
// coincidir con las etiquetas de la base. Se registran en ReliaDbContext.MapEnums.

public enum OperationType { Deposit, Withdrawal, FloatLoad }

public enum LoadMethod { BreB, Wallet }

public enum OperationStatus { Pending, Completed, Failed, Expired, Cancelled }

public enum SettlementStatus { Pending, Paid, Voided }

public enum PaymentRail { OpenPayments, BreBSimulated }

public enum PaymentDirection { Incoming, Outgoing }

public enum MessageChannel { Sms, Whatsapp }

public enum MessageType { Otp, WalletInvitation, DepositNotice, PaymentRequest, Receipt, CommissionNotice }

public enum LedgerEntryType { Deposit, Withdrawal, FloatLoad, Settlement, CashAdjustment }

public enum OtpPurpose { WalletLinking, ReceiptLookup }

public enum ReceiptIssueChannel { Screen, Pdf, Print, Whatsapp, Sms }

public enum ReceiptRequester { Agent, User, System }

public enum EventSource { Agent, System, Rail, Simulator }
