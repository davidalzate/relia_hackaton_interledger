using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Comercios que operan efectivo con Relia. Incluye su acceso y su wallet.
/// </summary>
public partial class Agent
{
    public Guid Id { get; set; }

    public string Email { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string ContactPerson { get; set; } = null!;

    public string City { get; set; } = null!;

    public string Address { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public string PaymentPointer { get; set; } = null!;

    public string Password { get; set; } = null!;

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual AgentBalance? AgentBalance { get; set; }

    public virtual ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();

    public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();

    public virtual ICollection<ReceiptIssuance> ReceiptIssuances { get; set; } = new List<ReceiptIssuance>();

    public virtual Settlement? Settlement { get; set; }
}
