using Microsoft.Extensions.Options;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services;

/// <summary>Ticket 07: consulta de llave e invitación por WhatsApp.</summary>
public class KeyService(
    IWalletKeyRepository keys, Messenger messenger, IUnitOfWork unitOfWork, IdempotencyStore idempotency,
    IOptions<ReliaOptions> relia, TimeProvider time) : IKeyService
{
    public async Task<KeyLookupDto> LookupAsync(string phone, CancellationToken ct = default)
    {
        var key = await keys.GetByMobileAsync(phone, ct) ?? throw KeyNotFound();
        return new KeyLookupDto(key.Mobile, Masking.Wallet(key.PaymentPointer));
    }

    public Task<KeyInvitationDto> InviteAsync(string phone, string idempotencyKey, CancellationToken ct = default) =>
        idempotency.RunAsync($"invitations|{phone}", idempotencyKey, phone, async () =>
        {
            if (await keys.GetByMobileAsync(phone, ct) is not null)
                throw ApiErrors.Conflict("key_already_linked", "El celular ya tiene una billetera vinculada");

            var link = $"{relia.Value.PublicLinkBaseUrl.TrimEnd('/')}/selectkey?phone={phone}";
            var message = $"Hola, te invito a vincular tu wallet en {link}";
            messenger.WhatsApp(phone, Db.MessageType.WalletInvitation, message,
                link.StartsWith("http", StringComparison.Ordinal) ? link : $"https://{link}");
            await unitOfWork.SaveChangesAsync(ct);

            return new KeyInvitationDto(phone, $"https://wa.me/57{phone}?text={Uri.EscapeDataString(message)}",
                message, time.GetUtcNow());
        });

    public static ApiException KeyNotFound() =>
        ApiErrors.NotFound("key_not_found", "El celular no tiene billetera vinculada");
}
