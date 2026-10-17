using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Rail;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services;

/// <summary>Ticket 08: vinculación pública (sin login) de un celular a un payment pointer con OTP.</summary>
public interface IKeyLinkService
{
    Task<KeyLinkChallengeDto> StartAsync(CreateKeyLinkRequest request, CancellationToken ct = default);
    Task<KeyLinkDto> VerifyAsync(Guid challengeId, VerifyKeyLinkRequest request, CancellationToken ct = default);
}

/// <summary>
/// OTP de 6 dígitos, 5 minutos y 5 intentos; se guarda solo su hash. Reenviar invalida el desafío anterior.
/// Rate limit: 5 códigos por celular cada 10 minutos.
/// </summary>
public class KeyLinkService(
    IWalletKeyRepository keys, IAgentRepository agents, IOtpChallengeRepository challenges, IRailClient rail,
    Messenger messenger, IUnitOfWork unitOfWork, RateLimiter rateLimiter, IOptions<ReliaOptions> relia,
    TimeProvider time) : IKeyLinkService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const short MaxAttempts = 5;

    public async Task<KeyLinkChallengeDto> StartAsync(CreateKeyLinkRequest request, CancellationToken ct = default)
    {
        await EnsureCanLinkAsync(request.Phone, request.WalletAddress, ct);
        if (await rail.ResolveWalletAddressAsync(request.WalletAddress, ct) is null)
            throw ApiErrors.Unprocessable("wallet_not_found", "El payment pointer no existe en el riel");
        rateLimiter.Hit($"key-links|{request.Phone}", 5, TimeSpan.FromMinutes(10));

        var now = time.UtcNow();
        foreach (var open in await challenges.ListOpenAsync(request.Phone, ct))
            if (open.ExpiresAt > now) open.ExpiresAt = now;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new Db.OtpChallenge
        {
            Id = Guid.NewGuid(), Mobile = request.Phone, Purpose = Db.OtpPurpose.WalletLinking,
            PaymentPointer = request.WalletAddress, Attempts = 0, MaxAttempts = MaxAttempts,
            ExpiresAt = now + Lifetime, CreatedAt = now,
        };
        challenge.CodeHash = Hash(challenge.Id, code);
        challenges.Add(challenge);
        messenger.Sms(request.Phone, Db.MessageType.Otp,
            $"Relia: tu código para vincular tu billetera es {code}. Vence en 5 minutos.");
        await unitOfWork.SaveChangesAsync(ct);

        return new KeyLinkChallengeDto(challenge.Id, challenge.Mobile, Clock.Utc(challenge.ExpiresAt), MaxAttempts);
    }

    public async Task<KeyLinkDto> VerifyAsync(Guid challengeId, VerifyKeyLinkRequest request,
        CancellationToken ct = default)
    {
        rateLimiter.Hit($"key-links-verify|{challengeId}", 20, TimeSpan.FromMinutes(10));
        var challenge = await challenges.GetByIdAsync(challengeId, ct);
        if (challenge is not { Purpose: Db.OtpPurpose.WalletLinking, PaymentPointer: not null })
            throw ApiErrors.NotFound();

        var now = time.UtcNow();
        if (challenge.Attempts >= challenge.MaxAttempts) throw AttemptsExceeded();
        if (challenge.VerifiedAt is not null || now >= challenge.ExpiresAt)
            throw ApiErrors.Unprocessable("otp_expired", "El código venció");

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Hash(challenge.Id, request.Code)), Encoding.ASCII.GetBytes(challenge.CodeHash)))
        {
            challenge.Attempts++;
            await unitOfWork.SaveChangesAsync(ct);
            var remaining = challenge.MaxAttempts - challenge.Attempts;
            if (remaining <= 0) throw AttemptsExceeded();
            throw ApiErrors.Unprocessable("invalid_otp", "Código incorrecto",
                new Dictionary<string, object?> { ["attemptsRemaining"] = remaining });
        }

        await EnsureCanLinkAsync(challenge.Mobile, challenge.PaymentPointer, ct);
        var key = new Db.WalletKey
        {
            Id = Guid.NewGuid(), Mobile = challenge.Mobile, PaymentPointer = challenge.PaymentPointer,
            WalletAddressUrl = "https://" + challenge.PaymentPointer[1..], LinkedAt = now,
        };
        keys.Add(key);
        challenge.VerifiedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        return new KeyLinkDto(key.Mobile, key.PaymentPointer, Clock.Utc(key.LinkedAt));
    }

    /// <summary>Reglas de la llave: permanente, única por celular y por billetera, separada de corresponsales y de Relia.</summary>
    private async Task EnsureCanLinkAsync(string phone, string wallet, CancellationToken ct)
    {
        if (await keys.GetByMobileAsync(phone, ct) is not null)
            throw ApiErrors.Conflict("key_already_linked", "El celular ya tiene una billetera vinculada");
        if (await agents.GetByPhoneAsync(phone, ct) is not null
            || wallet == relia.Value.WalletAddress
            || await agents.AnyWithPaymentPointerAsync(wallet, ct))
            throw ApiErrors.Conflict("phone_reserved", "El celular o la billetera están reservados");
        if (await keys.AnyWithPaymentPointerAsync(wallet, ct))
            throw ApiErrors.Conflict("wallet_already_linked", "La billetera ya está vinculada a otro celular");
    }

    private static ApiException AttemptsExceeded() =>
        ApiErrors.Unprocessable("otp_attempts_exceeded", "Se agotaron los intentos");

    private static string Hash(Guid challengeId, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId}:{code}")));
}
