using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Contenido congelado del recibo de cada depósito o retiro completado. Solo inserción.
/// </summary>
public partial class Receipt
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public string Code { get; set; } = null!;

    public string Data { get; set; } = null!;

    public string ContentHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Operation Operation { get; set; } = null!;

    public virtual ICollection<ReceiptIssuance> ReceiptIssuances { get; set; } = new List<ReceiptIssuance>();
}
