using Relia.Api.Contracts;
using Relia.Api.Repositories;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Support;

/// <summary>SMS y WhatsApp simulados: los deja en la bandeja del teléfono del demo. Quien llama guarda.</summary>
public class Messenger(ISimulatedMessageRepository messages, TimeProvider time)
{
    public void Sms(string phone, Db.MessageType type, string content, string? link = null, Guid? operationId = null) =>
        Send(Db.MessageChannel.Sms, phone, type, content, link, operationId);

    public void WhatsApp(string phone, Db.MessageType type, string content, string? link = null,
        Guid? operationId = null) =>
        Send(Db.MessageChannel.Whatsapp, phone, type, content, link, operationId);

    public void Send(Db.MessageChannel channel, string phone, Db.MessageType type, string content, string? link,
        Guid? operationId) =>
        messages.Add(new Db.SimulatedMessage
        {
            Channel = channel, Type = type, Recipient = phone, Content = content, Link = link,
            OperationId = operationId, IsRead = false, CreatedAt = time.UtcNow(),
        });

    public static DemoMessageDto ToDto(Db.SimulatedMessage m) => new(
        m.Id.ToString(), m.Recipient,
        m.Channel == Db.MessageChannel.Sms ? ReceiptSendChannel.Sms : ReceiptSendChannel.WhatsApp,
        m.Type switch
        {
            Db.MessageType.Otp => DemoMessageType.Otp,
            Db.MessageType.WalletInvitation => DemoMessageType.InvitacionWallet,
            Db.MessageType.DepositNotice => DemoMessageType.AvisoDeposito,
            Db.MessageType.PaymentRequest => DemoMessageType.SolicitudPago,
            Db.MessageType.Receipt => DemoMessageType.Comprobante,
            _ => DemoMessageType.AvisoComision,
        },
        m.Content, m.Link, m.OperationId, m.IsRead, Clock.Utc(m.CreatedAt));
}
