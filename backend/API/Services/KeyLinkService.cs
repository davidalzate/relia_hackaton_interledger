using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Relia.Api.Configuration;
using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Rail;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Vinculación pública: el usuario escribe su celular y su payment pointer, recibe un OTP por SMS simulado y, al
/// verificarlo, queda la llave (inmutable). El rate limit lo pone el controlador.
/// </summary>
public class KeyLinkService(
    IWalletKeyRepository keys, IAgentRepository agents, IOtpChallengeRepository challenges, IRailClient rail,
    ISimulatedInbox inbox, IUnitOfWork unitOfWork, IOptions<ReliaOptions> options, TimeProvider time,
    ILogger<KeyLinkService> logger) : IKeyLinkService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private const short MaxAttempts = 5;

    public async Task<KeyLinkChallengeDto> StartAsync(CreateKeyLinkRequest request, CancellationToken ct = default)
    {
        await EnsureCanLinkAsync(request.Phone, request.WalletAddress, ct);
        // No se rechaza por moneda: el riel puede convertir. Se devuelve para que el usuario la vea.
        RailWallet wallet;
        try
        {
            wallet = await rail.ResolveWalletAddressAsync(request.WalletAddress, ct);
        }
        catch (RailWalletNotFoundException)
        {
            throw ApiErrors.WalletNotFound();
        }
        catch (RailUnavailableException e)
        {
            logger.LogWarning(e, "No se pudo resolver {WalletAddress} en el riel", request.WalletAddress);
            throw ApiErrors.RailUnavailable();
        }
        catch (RailException e)
        {
            // El riel respondió, pero no acepta el pointer (p. ej. validation_error del proxy).
            logger.LogWarning(e, "El riel rechazó {WalletAddress} ({Code})", request.WalletAddress, e.Code);
            throw ApiErrors.WalletNotFound();
        }

        // Un desafío nuevo deja sin efecto los anteriores del mismo celular: verify solo acepta el más reciente.
        var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var now = time.GetUtcNow();
        var challenge = new OtpChallenge
        {
            Id = Guid.NewGuid(),
            Phone = request.Phone,
            Purpose = OtpPurpose.KeyLink,
            WalletAddress = request.WalletAddress,
            MaxAttempts = MaxAttempts,
            ExpiresAt = now + Lifetime,
            CreatedAt = now,
        };
        challenge.CodeHash = HashCode(challenge.Id, code);
        challenges.Add(challenge);
        inbox.Send(MessageChannel.Sms, MessageKind.Otp, request.Phone,
            $"Relia: tu código para vincular tu billetera es {code}. Vence en 5 minutos. No lo compartas.");
        await unitOfWork.SaveChangesAsync(ct);

        return new KeyLinkChallengeDto(challenge.Id, challenge.Phone, challenge.ExpiresAt, challenge.MaxAttempts,
            wallet.AssetCode);
    }

    public async Task<KeyLinkDto> VerifyAsync(Guid challengeId, VerifyKeyLinkRequest request,
        CancellationToken ct = default)
    {
        // Un intento fallido debe quedar guardado aunque se responda 422: el error se devuelve en vez de lanzarse,
        // para que la transacción confirme el contador, y se lanza después.
        (KeyLinkDto? Link, ApiException? Error) result;
        try
        {
            result = await unitOfWork.InTransactionAsync(async tx =>
            {
                var challenge = await challenges.GetForUpdateAsync(challengeId, OtpPurpose.KeyLink, tx)
                                ?? throw ApiErrors.NotFound();
                if (challenge.Attempts >= challenge.MaxAttempts)
                    return (null, ApiErrors.OtpAttemptsExceeded());
                var now = time.GetUtcNow();
                if (now >= challenge.ExpiresAt || await challenges.HasNewerAsync(challenge, tx))
                    return (null, ApiErrors.OtpExpired());
                if (!CodeMatches(challenge, request.Code))
                {
                    challenge.Attempts++;
                    return (null, ApiErrors.InvalidOtp(challenge.MaxAttempts - challenge.Attempts));
                }

                // Otro flujo pudo vincular el celular o la billetera mientras tanto.
                var wallet = challenge.WalletAddress!;
                await EnsureCanLinkAsync(challenge.Phone, wallet, tx);

                challenge.VerifiedAt = now;
                var key = new WalletKey { Id = Guid.NewGuid(), Phone = challenge.Phone, WalletAddress = wallet, LinkedAt = now };
                keys.Add(key);
                return ((KeyLinkDto?)new KeyLinkDto(key.Phone, key.WalletAddress, key.LinkedAt), (ApiException?)null);
            }, ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
                                          { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                          && pg.ConstraintName is "ux_wallet_keys_mobile" or "ux_wallet_keys_wallet")
        {
            // Carrera con otra verificación que insertó primero: la base decide con sus índices únicos.
            throw pg.ConstraintName == "ux_wallet_keys_mobile" ? ApiErrors.KeyAlreadyLinked() : ApiErrors.WalletAlreadyLinked();
        }

        return result.Link ?? throw result.Error!;
    }

    /// <summary>Validaciones de llave del contrato, en su orden; lanza el 409 que corresponde.</summary>
    private async Task EnsureCanLinkAsync(string phone, string walletAddress, CancellationToken ct)
    {
        if (await keys.GetByPhoneAsync(phone, ct) is not null) throw ApiErrors.KeyAlreadyLinked();

        var wallet = ToPointer(walletAddress);
        if (wallet == ToPointer(options.Value.WalletAddress) || await agents.UsesPhoneOrWalletAsync(phone, wallet, ct))
            throw ApiErrors.PhoneReserved();

        if (await keys.IsWalletLinkedAsync(wallet, ct)) throw ApiErrors.WalletAlreadyLinked();
    }

    /// <summary>Payment pointer en minúsculas y sin "/" final; acepta también la forma https:// del riel.</summary>
    private static string ToPointer(string walletAddress)
    {
        var pointer = walletAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? "$" + walletAddress["https://".Length..]
            : walletAddress;
        return pointer.TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>SHA-256 en hex del código, salado con el id del desafío. El código nunca se guarda en claro.</summary>
    private static string HashCode(Guid challengeId, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{challengeId:N}:{code}")));

    private static bool CodeMatches(OtpChallenge challenge, string code) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(HashCode(challenge.Id, code)), Encoding.ASCII.GetBytes(challenge.CodeHash));
}
