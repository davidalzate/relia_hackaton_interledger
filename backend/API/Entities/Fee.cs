using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Tarifa por operación, versionada. Cada operación copia sus valores.
/// </summary>
public partial class Fee
{
    public long Id { get; set; }

    public long TotalCommission { get; set; }

    public long ReliaCommission { get; set; }

    public long AgentCommission { get; set; }

    public DateTime ValidFrom { get; set; }

    public DateTime? ValidUntil { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();
}
