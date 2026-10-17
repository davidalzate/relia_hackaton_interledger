using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

public partial class VAgentHistory
{
    public Guid? AgentId { get; set; }

    public DateTime? OccurredAt { get; set; }

    public string? Type { get; set; }

    public string? Reference { get; set; }

    public Guid? OperationId { get; set; }

    public Guid? SettlementId { get; set; }

    public long? LedgerEntryId { get; set; }

    public long? Amount { get; set; }

    public long? FloatBefore { get; set; }

    public long? FloatAfter { get; set; }

    public long? CommissionBefore { get; set; }

    public long? CommissionAfter { get; set; }

    public long? CashBefore { get; set; }

    public long? CashAfter { get; set; }

    public string? Reason { get; set; }
}
