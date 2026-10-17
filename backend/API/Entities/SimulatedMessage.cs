using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Bandeja simulada de SMS y WhatsApp. Solo existe en el demo.
/// </summary>
public partial class SimulatedMessage
{
    public long Id { get; set; }

    public string Recipient { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? Link { get; set; }

    public Guid? OperationId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Operation? Operation { get; set; }
}
