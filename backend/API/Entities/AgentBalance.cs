using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Totales de cada corresponsal: cupo, comisión y efectivo esperado. Una fila por corresponsal.
/// </summary>
public partial class AgentBalance
{
    public Guid AgentId { get; set; }

    public long FloatBalance { get; set; }

    public long AccruedCommission { get; set; }

    public long SettledCommissionTotal { get; set; }

    public long ExpectedCash { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Agent Agent { get; set; } = null!;
}
