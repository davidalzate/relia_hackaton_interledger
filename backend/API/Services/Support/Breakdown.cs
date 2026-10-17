using Relia.Api.Contracts;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Support;

/// <summary>
/// Desglose de un depósito o retiro (reglas del esquema, M = monto solicitado):
/// sin valor redondo: neto = M − comisión, cobrado = M; con valor redondo: neto = M, cobrado = M + comisión.
/// </summary>
public record Breakdown(long Requested, bool Round, long FeeTotal, long FeeRelia, long FeeAgent, long Net, long Charged)
{
    public static Breakdown Of(long requested, bool round, Db.Fee fee) => new(
        requested, round, fee.TotalCommission, fee.ReliaCommission, fee.AgentCommission,
        round ? requested : requested - fee.TotalCommission,
        round ? requested + fee.TotalCommission : requested);

    /// <summary>El monto debe superar la comisión (el neto no puede quedar en cero o negativo).</summary>
    public bool TooLow => Requested <= FeeTotal;

    public FeeDto FeeDto => new(FeeTotal, FeeRelia, FeeAgent);

    /// <summary>
    /// Variaciones al completarse. Depósito: el cupo baja lo cobrado (efectivo recibido) y el efectivo sube lo mismo.
    /// Retiro: el cupo sube el neto y el efectivo baja el neto. En ambos la comisión del corresponsal se acumula.
    /// </summary>
    public (long Float, long Commission, long Cash) Changes(Db.OperationType type) => type switch
    {
        Db.OperationType.Deposit => (-Charged, FeeAgent, Charged),
        Db.OperationType.Withdrawal => (Net, FeeAgent, -Net),
        _ => (Net, 0, 0),
    };

    public static Breakdown Of(Db.Operation o) => new(
        o.RequestedAmount, o.RoundValue, o.TotalCommission, o.ReliaCommission, o.AgentCommission, o.NetAmount,
        o.ChargedAmount);

    /// <summary>Lo que falta para crear la operación con los saldos disponibles (descontadas las reservas).</summary>
    public IReadOnlyList<PreviewViolationDto> Violations(Db.OperationType type, Db.AgentBalance balance,
        Repositories.Reservations reserved)
    {
        var violations = new List<PreviewViolationDto>();
        if (TooLow) violations.Add(new PreviewViolationDto(PreviewViolationCode.AmountTooLow, null));
        if (type == Db.OperationType.Deposit)
        {
            var available = balance.FloatBalance - reserved.FloatReserved;
            if (available < Charged)
                violations.Add(new PreviewViolationDto(PreviewViolationCode.InsufficientQuota, Charged - available));
        }
        else
        {
            var available = balance.ExpectedCash - reserved.CashReserved;
            if (available < Net)
                violations.Add(new PreviewViolationDto(PreviewViolationCode.InsufficientCash, Net - available));
        }
        return violations;
    }
}
