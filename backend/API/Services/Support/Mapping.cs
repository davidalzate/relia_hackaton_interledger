using Relia.Api.Contracts;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Support;

/// <summary>Traducción entre los enums de la base (Entities) y los del contrato (Contracts).</summary>
public static class Mapping
{
    public static OperationStatus ToApi(this Db.OperationStatus status) => status switch
    {
        Db.OperationStatus.Pending => OperationStatus.Pending,
        Db.OperationStatus.Completed => OperationStatus.Completed,
        Db.OperationStatus.Failed => OperationStatus.Failed,
        Db.OperationStatus.Expired => OperationStatus.Expired,
        _ => OperationStatus.Cancelled,
    };

    public static Db.OperationStatus ToDb(this OperationStatus status) => status switch
    {
        OperationStatus.Pending => Db.OperationStatus.Pending,
        OperationStatus.Completed => Db.OperationStatus.Completed,
        OperationStatus.Failed => Db.OperationStatus.Failed,
        OperationStatus.Expired => Db.OperationStatus.Expired,
        _ => Db.OperationStatus.Cancelled,
    };

    public static OperationType ToApi(this Db.OperationType type) => type switch
    {
        Db.OperationType.Deposit => OperationType.Deposit,
        Db.OperationType.Withdrawal => OperationType.Withdrawal,
        _ => OperationType.QuotaTopUp,
    };

    public static Db.OperationType ToDb(this CashMovingOperationType type) =>
        type == CashMovingOperationType.Deposit ? Db.OperationType.Deposit : Db.OperationType.Withdrawal;

    public static TopUpMethod ToApi(this Db.LoadMethod method) =>
        method == Db.LoadMethod.BreB ? TopUpMethod.BreB : TopUpMethod.Wallet;

    public static Db.LoadMethod ToDb(this TopUpMethod method) =>
        method == TopUpMethod.BreB ? Db.LoadMethod.BreB : Db.LoadMethod.Wallet;

    public static SettlementStatus ToApi(this Db.SettlementStatus status) => status switch
    {
        Db.SettlementStatus.Pending => SettlementStatus.Pending,
        Db.SettlementStatus.Paid => SettlementStatus.Paid,
        _ => SettlementStatus.Voided,
    };

    public static Db.SettlementStatus ToDb(this SettlementStatus status) => status switch
    {
        SettlementStatus.Pending => Db.SettlementStatus.Pending,
        SettlementStatus.Paid => Db.SettlementStatus.Paid,
        _ => Db.SettlementStatus.Voided,
    };

    /// <summary>En el historial una liquidación pagada es completed y una anulada es cancelled.</summary>
    public static OperationStatus ToEntryStatus(this Db.SettlementStatus status) => status switch
    {
        Db.SettlementStatus.Pending => OperationStatus.Pending,
        Db.SettlementStatus.Paid => OperationStatus.Completed,
        _ => OperationStatus.Cancelled,
    };

    // ---- failureCode: se guarda en payments.error_code con el valor del contrato ----

    public const string CustomerRejected = "customer_rejected";
    public const string RequestExpired = "request_expired";
    public const string RequestCancelled = "request_cancelled";
    public const string PaymentFailed = "payment_failed";
    public const string RailUnavailable = "rail_unavailable";

    public static FailureCode? ToFailureCode(string? code) => code switch
    {
        CustomerRejected => FailureCode.CustomerRejected,
        RequestExpired => FailureCode.RequestExpired,
        RequestCancelled => FailureCode.RequestCancelled,
        PaymentFailed => FailureCode.PaymentFailed,
        RailUnavailable => FailureCode.RailUnavailable,
        _ => null,
    };

    public static string Reference(this Db.Operation o) => o.DisplayNumber ?? $"OP-{o.Number:D6}";

    public static string Reference(this Db.Settlement s) => s.DisplayNumber ?? $"LQ-{s.Number:D6}";

    public static Db.Payment? LatestPayment(this Db.Operation o) =>
        o.Payments.OrderByDescending(p => p.CreatedAt).FirstOrDefault();

    public static BalancePairDto Pair(long before, long after) => new(before, after);

    /// <summary>Saldos sin cambio (entradas no completadas): antes = después.</summary>
    public static BalanceChangeDto Unchanged(long quota, long commission, long cash) =>
        new(Pair(quota, quota), Pair(commission, commission), Pair(cash, cash));

    public static BalanceChangeDto ToChange(this Db.LedgerEntry e) =>
        new(Pair(e.FloatBefore, e.FloatAfter), Pair(e.CommissionBefore, e.CommissionAfter), Pair(e.CashBefore, e.CashAfter));

    public static BalanceChangeDto Unchanged(this Db.AgentBalance b) =>
        Unchanged(b.FloatBalance, b.AccruedCommission, b.ExpectedCash);
}
