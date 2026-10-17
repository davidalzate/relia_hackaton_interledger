using System.Globalization;
using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Mensajería simulada sobre simulated_messages: todos los SMS y WhatsApp del backend pasan por aquí y la
/// bandeja del teléfono simulado (/demo/messages) los lee de aquí.
/// </summary>
public interface ISimulatedInbox
{
    /// <summary>Agrega el mensaje sin guardar: se confirma con la unidad de trabajo de quien lo envía.</summary>
    SimulatedMessage Send(MessageChannel channel, MessageKind kind, string phone, string content,
        string? link = null, Guid? operationId = null);

    /// <summary>El más reciente primero; unreadCount con el mismo filtro de celular.</summary>
    Task<DemoMessagePageDto> ListAsync(string? phone, string? cursor, int limit, CancellationToken ct = default);

    Task MarkReadAsync(string? phone, CancellationToken ct = default);
}

public class SimulatedInbox(ISimulatedMessageRepository messages, TimeProvider time) : ISimulatedInbox
{
    // Tipos de la base → tipos del contrato.
    private static readonly Dictionary<MessageKind, DemoMessageType> Types = new()
    {
        [MessageKind.Otp] = DemoMessageType.Otp,
        [MessageKind.WalletInvitation] = DemoMessageType.WalletInvitation,
        [MessageKind.DepositNotice] = DemoMessageType.DepositNotice,
        [MessageKind.PaymentRequest] = DemoMessageType.PaymentRequest,
        [MessageKind.Receipt] = DemoMessageType.Receipt,
        [MessageKind.CommissionNotice] = DemoMessageType.CommissionNotice,
    };

    public SimulatedMessage Send(MessageChannel channel, MessageKind kind, string phone, string content,
        string? link = null, Guid? operationId = null)
    {
        var message = new SimulatedMessage
        {
            Channel = channel,
            Kind = kind,
            Recipient = phone,
            Content = content,
            Link = link,
            OperationId = operationId,
            CreatedAt = time.GetUtcNow(),
        };
        messages.Add(message);
        return message;
    }

    public async Task<DemoMessagePageDto> ListAsync(string? phone, string? cursor, int limit,
        CancellationToken ct = default)
    {
        // Cursor = id del último mensaje de la página; los ids crecen con el tiempo.
        long? beforeId = null;
        if (cursor is not null)
        {
            if (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                throw ApiErrors.Validation("cursor", "Cursor inválido.");
            beforeId = id;
        }

        // Una fila de más para saber si hay página siguiente.
        var rows = await messages.ListAsync(phone, beforeId, limit + 1, ct);
        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? page[^1].Id.ToString(CultureInfo.InvariantCulture) : null;
        var unread = await messages.CountUnreadAsync(phone, ct);
        return new DemoMessagePageDto(page.Select(ToDto).ToList(), unread, next);
    }

    public Task MarkReadAsync(string? phone, CancellationToken ct = default) => messages.MarkReadAsync(phone, ct);

    private static DemoMessageDto ToDto(SimulatedMessage m) => new(
        m.Id.ToString(CultureInfo.InvariantCulture),
        m.Recipient,
        m.Channel == MessageChannel.WhatsApp ? DemoMessageChannel.WhatsApp : DemoMessageChannel.Sms,
        Types[m.Kind],
        m.Content,
        m.Link,
        m.OperationId,
        m.Read,
        m.CreatedAt);
}
