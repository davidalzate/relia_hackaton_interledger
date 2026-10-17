using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Pago manual mensual de la comisión total pendiente, a la wallet del corresponsal.
/// </summary>
public partial class Settlement
{
    public Guid Id { get; set; }

    public long Number { get; set; }

    public string? DisplayNumber { get; set; }

    public Guid AgentId { get; set; }

    public Guid CreatedByAdminId { get; set; }

    public string DestinationPaymentPointer { get; set; } = null!;

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public long Amount { get; set; }

    public DateTime? PaidAt { get; set; }

    public string? VoidReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Agent Agent { get; set; } = null!;

    public virtual Admin CreatedByAdmin { get; set; } = null!;

    public virtual LedgerEntry? LedgerEntry { get; set; }

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
