using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Administradores de Relia. Acceden al panel y pagan las comisiones.
/// </summary>
public partial class Admin
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Password { get; set; } = null!;

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Settlement> Settlements { get; set; } = new List<Settlement>();
}
