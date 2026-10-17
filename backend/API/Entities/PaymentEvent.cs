using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Bitácora de solo inserción de cada pago: llamadas al riel, respuestas y cambios de estado.
/// </summary>
public partial class PaymentEvent
{
    public long Id { get; set; }

    public Guid PaymentId { get; set; }

    public string Action { get; set; } = null!;

    public short? HttpStatus { get; set; }

    public string? RailResponse { get; set; }

    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Payment Payment { get; set; } = null!;
}
