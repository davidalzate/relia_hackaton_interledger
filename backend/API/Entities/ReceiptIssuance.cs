using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Registro de cada vez que un recibo se muestra, descarga o envía. Desde la segunda es copia.
/// </summary>
public partial class ReceiptIssuance
{
    public long Id { get; set; }

    public Guid ReceiptId { get; set; }

    public Guid? AgentId { get; set; }

    public string? UserMobile { get; set; }

    public bool IsCopy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Agent? Agent { get; set; }

    public virtual Receipt Receipt { get; set; } = null!;
}
