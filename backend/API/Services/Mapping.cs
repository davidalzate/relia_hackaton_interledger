using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Rail;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>De entidades de la base a DTOs del contrato. Compartido por los servicios.</summary>
public static class Mapping
{
    /// <summary>Requiere el corresponsal con Balance cargado.</summary>
    public static AgentDto ToDto(this Agent agent, Reservations reservations)
    {
        var balance = agent.Balance
                      ?? throw new InvalidOperationException($"El corresponsal {agent.Id} no tiene agent_balances");
        return new AgentDto(
            agent.Id, agent.Name, agent.ManagerName, agent.City, agent.Address, agent.Phone, agent.WalletAddress,
            new AgentBalancesDto(
                balance.Quota, reservations.QuotaReserved, balance.CashOnHand, reservations.CashReserved,
                balance.AccruedCommission, balance.SettledCommissionTotal));
    }

    /// <summary>failure_code de la base → FailureCode del contrato.</summary>
    public static readonly IReadOnlyDictionary<string, FailureCode> FailureCodes = new Dictionary<string, FailureCode>
    {
        ["customer_rejected"] = FailureCode.CustomerRejected,
        ["request_expired"] = FailureCode.RequestExpired,
        ["request_cancelled"] = FailureCode.RequestCancelled,
        ["payment_failed"] = FailureCode.PaymentFailed,
        ["rail_unavailable"] = FailureCode.RailUnavailable,
    };

    /// <summary>
    /// Requiere la operación con WalletKey cargada (depósito y retiro). <paramref name="payment"/> es el último
    /// pago de la operación: de él salen approvalUrl y expiresAt mientras está pendiente, y failureDetail al fallar.
    /// </summary>
    public static OperationDto ToDto(this Operation o, Payment? payment, BalanceChangeDto balances)
    {
        var pending = o.State == OperationState.Pending;
        return new OperationDto(
            o.Id, o.Reference, o.AgentId,
            o.Kind switch
            {
                OperationKind.Deposit => OperationType.Deposit,
                OperationKind.Withdrawal => OperationType.Withdrawal,
                _ => OperationType.QuotaTopUp,
            },
            ToStatus(o.State),
            o.FailureCode is null ? null : FailureCodes[o.FailureCode],
            o.FailureCode is null ? null : payment?.ErrorMessage,
            o.CreatedAt, o.CompletedAt,
            pending ? payment?.ExpiresAt : null,
            o.RequestedAmount, o.RoundAmount, new FeeDto(o.FeeTotal, o.FeeRelia, o.FeeAgent), o.NetAmount,
            o.ChargedAmount,
            o.WalletKey is null
                ? null
                : new OperationCustomerDto(Masking.Phone(o.WalletKey.Phone), Masking.WalletAddress(o.WalletKey.WalletAddress)),
            o.TopUpMethod switch
            {
                TopUpMethodKind.BreB => TopUpMethod.BreB,
                TopUpMethodKind.Wallet => TopUpMethod.Wallet,
                _ => null,
            },
            pending ? payment?.ApprovalUrl : null,
            QrPayload: pending ? payment?.SimulatedQrPayload() : null,
            CashHandedOverAt: o.CashHandedOverAt,
            Balances: balances,
            WalletAmount: payment?.WalletAmount());
    }

    /// <summary>
    /// Monto en la moneda de la billetera de la contraparte, o null si es COP o el riel aún no lo informa. Relia paga
    /// (outgoing): lo recibido o, mientras procesa, lo cotizado. Relia cobra (incoming): lo debitado al pagador.
    /// </summary>
    public static WalletAmountDto? WalletAmount(this Payment payment) => payment.Direction == PaymentDirection.Outgoing
        ? ToWalletAmount(payment.ReceivedValue, payment.ReceivedAssetCode, payment.ReceivedAssetScale)
          ?? ToWalletAmount(payment.QuotedReceiveValue, payment.QuotedReceiveAssetCode, payment.QuotedReceiveAssetScale)
        : ToWalletAmount(payment.DebitValue, payment.DebitAssetCode, payment.DebitAssetScale);

    /// <summary>Unidades mínimas del riel ("1234", escala 2) → "12.34". Null si falta o la moneda es COP.</summary>
    public static WalletAmountDto? ToWalletAmount(string? value, string? assetCode, int? assetScale)
    {
        if (value is null || assetCode is null || assetScale is not { } scale || assetCode == RailAmount.Cop
            || scale < 0 || !BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var units))
            return null;
        if (scale == 0) return new WalletAmountDto(units.ToString(CultureInfo.InvariantCulture), assetCode);
        var whole = BigInteger.DivRem(units, BigInteger.Pow(10, scale), out var fraction);
        return new WalletAmountDto(
            $"{whole.ToString(CultureInfo.InvariantCulture)}.{fraction.ToString(CultureInfo.InvariantCulture).PadLeft(scale, '0')}",
            assetCode);
    }

    /// <summary>
    /// Saldos antes y después: los del movimiento si la operación lo tiene; si no, antes = después con el último
    /// saldo conocido (la misma regla que v_agent_history).
    /// </summary>
    public static BalanceChangeDto ToBalanceChange(LedgerEntry? own, LedgerEntry? lastKnown)
    {
        if (own is not null)
            return new BalanceChangeDto(
                new BalancePairDto(own.QuotaBefore, own.QuotaAfter),
                new BalancePairDto(own.CommissionBefore, own.CommissionAfter),
                new BalancePairDto(own.CashBefore, own.CashAfter));
        var quota = lastKnown?.QuotaAfter ?? 0;
        var commission = lastKnown?.CommissionAfter ?? 0;
        var cash = lastKnown?.CashAfter ?? 0;
        return new BalanceChangeDto(
            new BalancePairDto(quota, quota), new BalancePairDto(commission, commission), new BalancePairDto(cash, cash));
    }

    private const string QrPayloadKey = "qrPayload";

    /// <summary>rail_details de un pago Bre-B simulado: marca de simulado y el QR que se muestra.</summary>
    public static JsonDocument SimulatedRailDetail(string qrPayload) => JsonSerializer.SerializeToDocument(
        new Dictionary<string, object?> { ["simulated"] = true, [QrPayloadKey] = qrPayload });

    public static string? SimulatedQrPayload(this Payment payment) =>
        payment.Rail == PaymentRail.BreBSimulated
        && payment.RailDetail.RootElement.TryGetProperty(QrPayloadKey, out var qr)
        && qr.ValueKind == JsonValueKind.String
            ? qr.GetString()
            : null;

    public static OperationStatus ToStatus(OperationState state) => state switch
    {
        OperationState.Pending => OperationStatus.Pending,
        OperationState.Completed => OperationStatus.Completed,
        OperationState.Failed => OperationStatus.Failed,
        OperationState.Expired => OperationStatus.Expired,
        _ => OperationStatus.Cancelled,
    };
}
