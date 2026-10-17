using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

public partial class VWalletTraceability
{
    public string? PaymentPointer { get; set; }

    public string? Side { get; set; }

    public Guid? PaymentId { get; set; }

    public long? Amount { get; set; }

    public string? Currency { get; set; }

    public string? SourcePaymentPointer { get; set; }

    public string? DestinationPaymentPointer { get; set; }

    public Guid? OperationId { get; set; }

    public string? OperationNumber { get; set; }

    public Guid? SettlementId { get; set; }

    public string? SettlementNumber { get; set; }

    public string? OpIncomingPaymentUrl { get; set; }

    public string? OpOutgoingPaymentUrl { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
