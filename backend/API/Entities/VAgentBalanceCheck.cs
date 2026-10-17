using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

public partial class VAgentBalanceCheck
{
    public Guid? AgentId { get; set; }

    public string? Name { get; set; }

    public long? FloatBalance { get; set; }

    public long? AccruedCommission { get; set; }

    public long? SettledCommissionTotal { get; set; }

    public long? ExpectedCash { get; set; }

    public decimal? CashReceivedDeposits { get; set; }

    public decimal? CashPaidWithdrawals { get; set; }

    public decimal? CashAdjustments { get; set; }

    public decimal? FloatLoaded { get; set; }

    public decimal? CommissionEarned { get; set; }

    public decimal? CashCalculated { get; set; }

    public decimal? FloatCalculated { get; set; }

    public decimal? CashMismatch { get; set; }

    public decimal? FloatMismatch { get; set; }

    public decimal? CommissionMismatch { get; set; }
}
