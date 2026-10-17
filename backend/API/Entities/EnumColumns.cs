namespace Relia.Api.Entities;

// Columnas de tipo enum que el scaffold omite. Manual: sobrevive a un nuevo scaffold.
// El mapeo a columnas está en Persistence/ReliaDbContext.Enums.cs.

public partial class Operation
{
    public OperationType Type { get; set; }
    public LoadMethod? LoadMethod { get; set; }
    public OperationStatus Status { get; set; }
}

public partial class Payment
{
    public PaymentRail Rail { get; set; }
    public PaymentDirection Direction { get; set; }
    public OperationStatus Status { get; set; }
}

public partial class Settlement
{
    public SettlementStatus Status { get; set; }
}

public partial class SimulatedMessage
{
    public MessageChannel Channel { get; set; }
    public MessageType Type { get; set; }
}

public partial class LedgerEntry
{
    public LedgerEntryType Type { get; set; }
}

public partial class OtpChallenge
{
    public OtpPurpose Purpose { get; set; }
}

public partial class ReceiptIssuance
{
    public ReceiptIssueChannel Channel { get; set; }
    public ReceiptRequester RequestedBy { get; set; }
}

public partial class OperationEvent
{
    public OperationStatus? PreviousStatus { get; set; }
    public OperationStatus NewStatus { get; set; }
    public EventSource Source { get; set; }
}

public partial class PaymentEvent
{
    public OperationStatus? PreviousStatus { get; set; }
    public OperationStatus NewStatus { get; set; }
    public EventSource Source { get; set; }
}
