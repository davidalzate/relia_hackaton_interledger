using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

public partial class VAdminAgentSummary
{
    public Guid? AgentId { get; set; }

    public string? Name { get; set; }

    public string? ContactPerson { get; set; }

    public string? City { get; set; }

    public string? PaymentPointer { get; set; }

    public long? FloatBalance { get; set; }

    public long? ExpectedCash { get; set; }

    public long? CommissionPayable { get; set; }

    public long? CommissionPaidTotal { get; set; }

    public long? OperationsCount { get; set; }

    public long? DepositsCount { get; set; }

    public decimal? DepositsValue { get; set; }

    public long? WithdrawalsCount { get; set; }

    public decimal? WithdrawalsValue { get; set; }

    public decimal? TotalMoved { get; set; }

    public decimal? CommissionPaidToday { get; set; }
}
