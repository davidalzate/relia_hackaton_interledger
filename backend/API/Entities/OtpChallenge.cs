using System;
using System.Collections.Generic;

namespace Relia.Api.Entities;

/// <summary>
/// OTP de vinculación de wallet y de consulta de recibos. Código guardado como hash, con expiración e intentos.
/// </summary>
public partial class OtpChallenge
{
    public Guid Id { get; set; }

    public string Mobile { get; set; } = null!;

    public string? PaymentPointer { get; set; }

    public string CodeHash { get; set; } = null!;

    public short Attempts { get; set; }

    public short MaxAttempts { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
