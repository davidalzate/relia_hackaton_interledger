using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Support;

/// <summary>
/// Contenido congelado del recibo (receipts.data). Guarda celular y billetera completos; se enmascaran al
/// responder. En Postgres un trigger puede congelarlo antes con su propio formato: en ese caso el recibo se
/// arma desde la operación (ver ReceiptService).
/// </summary>
public record ReceiptSnapshot(
    string Reference, string Type, DateTimeOffset Date, string Currency,
    string MerchantName, string MerchantAddress, string MerchantPhone,
    string CustomerPhone, string CustomerWallet,
    long RequestedAmount, bool RoundAmount, long Fee, long NetAmount, long ChargedAmount,
    string Rail, string RailReference)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ReceiptSnapshot Of(Db.Operation o, Db.Agent agent, Db.WalletKey key, Db.Payment payment,
        DateTime completedAt) => new(
        o.Reference(), o.Type == Db.OperationType.Deposit ? "deposit" : "withdrawal", Clock.Utc(completedAt), "COP",
        agent.Name, agent.Address, agent.Phone, key.Mobile, key.PaymentPointer,
        o.RequestedAmount, o.RoundValue, o.TotalCommission, o.NetAmount, o.ChargedAmount,
        payment.Rail == Db.PaymentRail.OpenPayments ? "openPayments" : "breBSimulated",
        payment.OpOutgoingPaymentUrl ?? payment.OpIncomingPaymentUrl ?? payment.RailReference ?? payment.Id.ToString());

    public Db.Receipt Freeze(Guid operationId, DateTime now)
    {
        var data = JsonSerializer.Serialize(this, Json);
        return new Db.Receipt
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            Code = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)),
            Data = data,
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data))),
            CreatedAt = now,
        };
    }

    /// <summary>Null si el contenido no lo escribió este backend (por ejemplo, el trigger de Postgres).</summary>
    public static ReceiptSnapshot? TryRead(string data)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<ReceiptSnapshot>(data, Json);
            return snapshot?.Reference is null || snapshot.MerchantName is null ? null : snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
