using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Depósitos, retiros y cargas de cupo. Copia la tarifa y los montos calculados. Sus datos no cambian después de creada.
/// </summary>
public partial class Operation
{
    public Guid Id { get; set; }

    public long Number { get; set; }

    public string? DisplayNumber { get; set; }

    public Guid AgentId { get; set; }

    public Guid? WalletKeyId { get; set; }

    public long RequestedAmount { get; set; }

    public bool RoundValue { get; set; }

    public long? FeeId { get; set; }

    public long TotalCommission { get; set; }

    public long ReliaCommission { get; set; }

    public long AgentCommission { get; set; }

    public long NetAmount { get; set; }

    public long ChargedAmount { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Agent Agent { get; set; } = null!;

    public virtual Fee? Fee { get; set; }

    public virtual LedgerEntry? LedgerEntry { get; set; }

    public virtual ICollection<OperationEvent> OperationEvents { get; set; } = new List<OperationEvent>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public virtual Receipt? Receipt { get; set; }

    public virtual ICollection<SimulatedMessage> SimulatedMessages { get; set; } = new List<SimulatedMessage>();

    public virtual WalletKey? WalletKey { get; set; }
}
