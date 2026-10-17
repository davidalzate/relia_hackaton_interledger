using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Libro de solo inserción del cupo, la comisión y el efectivo: antes, variación y después.
/// </summary>
public partial class LedgerEntry
{
    public long Id { get; set; }

    public Guid AgentId { get; set; }

    public Guid? OperationId { get; set; }

    public Guid? SettlementId { get; set; }

    public Guid? PaymentId { get; set; }

    public long FloatBefore { get; set; }

    public long FloatChange { get; set; }

    public long FloatAfter { get; set; }

    public long CommissionBefore { get; set; }

    public long CommissionChange { get; set; }

    public long CommissionAfter { get; set; }

    public long CashBefore { get; set; }

    public long CashChange { get; set; }

    public long CashAfter { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Agent Agent { get; set; } = null!;

    public virtual Operation? Operation { get; set; }

    public virtual Payment? Payment { get; set; }

    public virtual Settlement? Settlement { get; set; }
}
