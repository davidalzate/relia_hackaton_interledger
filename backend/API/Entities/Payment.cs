using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// Intentos de pago por riel (Open Payments o Bre-B simulado) de operaciones y liquidaciones.
/// </summary>
public partial class Payment
{
    public Guid Id { get; set; }

    public Guid? OperationId { get; set; }

    public Guid? SettlementId { get; set; }

    public long Amount { get; set; }

    public string Currency { get; set; } = null!;

    public string SourcePaymentPointer { get; set; } = null!;

    public string DestinationPaymentPointer { get; set; } = null!;

    public string? OpIncomingPaymentUrl { get; set; }

    public string? OpQuoteUrl { get; set; }

    public string? OpOutgoingPaymentUrl { get; set; }

    public string? ApprovalUrl { get; set; }

    public string? GrantContinueUri { get; set; }

    public byte[]? GrantContinueTokenEncrypted { get; set; }

    public string? RailReference { get; set; }

    public string? DebitValue { get; set; }

    public string? DebitAssetCode { get; set; }

    public short? DebitAssetScale { get; set; }

    public string? ReceivedValue { get; set; }

    public string? ReceivedAssetCode { get; set; }

    public short? ReceivedAssetScale { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string RailDetails { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual LedgerEntry? LedgerEntry { get; set; }

    public virtual Operation? Operation { get; set; }

    public virtual ICollection<PaymentEvent> PaymentEvents { get; set; } = new List<PaymentEvent>();

    public virtual Settlement? Settlement { get; set; }
}
