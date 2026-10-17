using Microsoft.Extensions.Options;
using Relia.Api.Configuration;
using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// GET /keys/{phone} y POST /keys/{phone}/invitations. La idempotencia de la invitación la resuelve el filtro
/// [Idempotent] del controlador, así que la clave no se usa aquí.
/// </summary>
public class KeyService(
    IWalletKeyRepository keys, ISimulatedInbox inbox, IUnitOfWork unitOfWork, IOptions<ReliaOptions> options)
    : IKeyService
{
    /// <summary>Ruta de la página pública de vinculación en el frontend; lee el celular de ?phone=.</summary>
    private const string KeyLinkPath = "/vincular";

    /// <summary>Indicativo de Colombia para wa.me.</summary>
    private const string CountryCode = "57";

    public async Task<KeyLookupDto> LookupAsync(string phone, CancellationToken ct = default)
    {
        var key = await keys.GetByPhoneAsync(phone, ct) ?? throw ApiErrors.KeyNotFound();
        return new KeyLookupDto(key.Phone, Masking.WalletAddress(key.WalletAddress));
    }

    public Task<KeyInvitationDto> InviteAsync(string phone, string idempotencyKey, CancellationToken ct = default) =>
        unitOfWork.InTransactionAsync(async tx =>
        {
            if (await keys.GetByPhoneAsync(phone, tx) is not null) throw ApiErrors.KeyAlreadyLinked();

            var link = $"{options.Value.PublicBaseUrl.TrimEnd('/')}{KeyLinkPath}?phone={phone}";
            var text = $"Hola, te invito a vincular tu wallet en {link}";
            var message = inbox.Send(MessageChannel.WhatsApp, MessageKind.WalletInvitation, phone, text, link);
            var waLink = $"https://wa.me/{CountryCode}{phone}?text={Uri.EscapeDataString(text)}";
            return new KeyInvitationDto(phone, waLink, text, message.CreatedAt);
        }, ct);
}
