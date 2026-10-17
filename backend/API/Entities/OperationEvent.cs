using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Bitácora de solo inserción con cada cambio de estado de una operación.
/// </summary>
public partial class OperationEvent
{
    public long Id { get; set; }

    public Guid OperationId { get; set; }

    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Operation Operation { get; set; } = null!;
}
