using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Celular vinculado a un payment pointer. Inmutable. Borrar solo con TRUNCATE en el reinicio del demo.
/// </summary>
public partial class WalletKey
{
    public Guid Id { get; set; }

    public string Mobile { get; set; } = null!;

    public string PaymentPointer { get; set; } = null!;

    public string? WalletAddressUrl { get; set; }

    public DateTime LinkedAt { get; set; }

    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();
}
