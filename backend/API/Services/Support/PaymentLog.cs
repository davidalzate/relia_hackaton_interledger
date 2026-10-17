using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Relia.Api.Rail;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Support;

/// <summary>Bitácora del pago (payment_events): cada respuesta del riel y cada cambio de estado.</summary>
public static class PaymentLog
{
    /// <summary>Registra una respuesta del riel tal como llegó, cambie o no el estado.</summary>
    public static void RailResponse(Db.Payment payment, string action, RailPayment rp, DateTime now) =>
        payment.PaymentEvents.Add(new Db.PaymentEvent
        {
            PaymentId = payment.Id, Action = action, PreviousStatus = payment.Status, NewStatus = payment.Status,
            Source = Db.EventSource.Rail, RailResponse = JsonSerializer.Serialize(rp), CreatedAt = now,
        });

    public static void SetStatus(Db.Payment payment, Db.OperationStatus status, Db.EventSource source,
        string action, DateTime now)
    {
        payment.PaymentEvents.Add(new Db.PaymentEvent
        {
            PaymentId = payment.Id, Action = action, PreviousStatus = payment.Status, NewStatus = status,
            Source = source, CreatedAt = now,
        });
        payment.Status = status;
        payment.UpdatedAt = now;
    }

    /// <summary>
    /// Id del pago derivado de la clave de idempotencia. Es la Idempotency-Key ante el riel: si la base no
    /// alcanzó a guardar un pago que el riel ya aceptó, reintentar con la misma clave lo recupera en vez de
    /// crear otro.
    /// </summary>
    public static Guid PaymentId(string scope, string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}|{idempotencyKey}"));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // versión 5 (basado en nombre)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variante RFC 4122
        return new Guid(bytes, bigEndian: true);
    }
}
