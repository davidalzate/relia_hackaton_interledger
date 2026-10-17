using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>Montos de un depósito o retiro con la tarifa vigente, tal como los guarda la operación.</summary>
public record Breakdown(long Total, long ReliaShare, long AgentShare, long NetAmount, long ChargedAmount);

/// <summary>
/// Reglas de montos de depósito y retiro, sin acceso a datos: las usan el desglose en vivo y la creación de
/// la operación. Son las mismas que valida la base (ck_op_deposit_withdrawal y validate_ledger_entry_payment).
/// </summary>
public static class OperationRules
{
    /// <summary>El contrato pide un monto solicitado mayor a $1.000 en depósito y retiro.</summary>
    public const long MinimumRequestedAmount = 1000;

    /// <summary>
    /// Con valor redondo, el usuario recibe el monto y paga la tarifa aparte; sin él, la tarifa se descuenta.
    /// </summary>
    public static Breakdown Calculate(long requestedAmount, bool roundAmount, Fee fee)
    {
        var net = roundAmount ? requestedAmount : requestedAmount - fee.Total;
        return new Breakdown(fee.Total, fee.ReliaShare, fee.AgentShare, net, net + fee.Total);
    }

    /// <summary>
    /// Saldos después de la operación y reglas incumplidas. El depósito consume cupo (lo cobrado) y suma
    /// efectivo; el retiro consume efectivo (el neto) y suma cupo. Lo reservado por pendientes no está disponible.
    /// </summary>
    public static OperationPreviewDto Preview(
        CashMovingOperationType type, long requestedAmount, bool roundAmount, Fee fee,
        AgentBalance balance, Reservations reserved)
    {
        var amounts = Calculate(requestedAmount, roundAmount, fee);
        var violations = new List<PreviewViolationDto>();
        if (requestedAmount <= Math.Max(fee.Total, MinimumRequestedAmount))
            violations.Add(new PreviewViolationDto(PreviewViolationCode.AmountTooLow, null));

        long quotaAfter, cashAfter;
        if (type == CashMovingOperationType.Deposit)
        {
            quotaAfter = balance.Quota - reserved.QuotaReserved - amounts.ChargedAmount;
            cashAfter = balance.CashOnHand + amounts.ChargedAmount;
            if (quotaAfter < 0)
                violations.Add(new PreviewViolationDto(PreviewViolationCode.InsufficientQuota, -quotaAfter));
        }
        else
        {
            quotaAfter = balance.Quota + amounts.NetAmount;
            cashAfter = balance.CashOnHand - reserved.CashReserved - amounts.NetAmount;
            if (cashAfter < 0)
                violations.Add(new PreviewViolationDto(PreviewViolationCode.InsufficientCash, -cashAfter));
        }

        return new OperationPreviewDto(
            type, requestedAmount, roundAmount,
            new FeeDto(amounts.Total, amounts.ReliaShare, amounts.AgentShare),
            amounts.NetAmount, amounts.ChargedAmount, quotaAfter, cashAfter,
            balance.AccruedCommission + amounts.AgentShare, violations);
    }
}
