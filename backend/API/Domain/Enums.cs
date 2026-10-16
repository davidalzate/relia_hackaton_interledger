using NpgsqlTypes;

namespace Relia.Api.Domain;

// Enums de PostgreSQL del esquema v0.2. PgName da la etiqueta en la base.

public enum OperationKind
{
    [PgName("deposito")] Deposit,
    [PgName("retiro")] Withdrawal,
    [PgName("carga_cupo")] QuotaTopUp,
}

public enum TopUpMethodKind
{
    [PgName("bre_b")] BreB,
    [PgName("wallet")] Wallet,
}

public enum OperationState
{
    [PgName("pendiente")] Pending,
    [PgName("completada")] Completed,
    [PgName("fallida")] Failed,
    [PgName("vencida")] Expired,
    [PgName("cancelada")] Cancelled,
}

public enum SettlementState
{
    [PgName("pendiente")] Pending,
    [PgName("pagada")] Paid,
    [PgName("anulada")] Voided,
}

public enum PaymentRail
{
    [PgName("open_payments")] OpenPayments,
    [PgName("bre_b_simulado")] BreBSimulated,
}

public enum PaymentDirection
{
    [PgName("entrante")] Incoming,
    [PgName("saliente")] Outgoing,
}

public enum MessageChannel
{
    [PgName("sms")] Sms,
    [PgName("whatsapp")] WhatsApp,
}

public enum MessageKind
{
    [PgName("otp")] Otp,
    [PgName("invitacion_wallet")] WalletInvitation,
    [PgName("aviso_deposito")] DepositNotice,
    [PgName("solicitud_pago")] PaymentRequest,
    [PgName("comprobante")] Receipt,
    [PgName("aviso_comision")] CommissionNotice,
}

public enum LedgerEntryKind
{
    [PgName("deposito")] Deposit,
    [PgName("retiro")] Withdrawal,
    [PgName("carga_cupo")] QuotaTopUp,
    [PgName("liquidacion")] Settlement,
    [PgName("ajuste_efectivo")] CashAdjustment,
}

public enum OtpPurpose
{
    [PgName("vinculacion")] KeyLink,
    [PgName("consulta_recibo")] ReceiptLookup,
}

public enum ReceiptChannel
{
    [PgName("pantalla")] Screen,
    [PgName("pdf")] Pdf,
    [PgName("impresion")] Print,
    [PgName("whatsapp")] WhatsApp,
    [PgName("sms")] Sms,
}

public enum ReceiptRequester
{
    [PgName("corresponsal")] Agent,
    [PgName("usuario")] Customer,
    [PgName("sistema")] System,
}

public enum EventOrigin
{
    [PgName("corresponsal")] Agent,
    [PgName("sistema")] System,
    [PgName("riel")] Rail,
    [PgName("simulador")] Simulator,
}
