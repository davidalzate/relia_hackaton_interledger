using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Recibos congelados de depósitos y retiros. La base congela el recibo al completar (issue_receipt) y marca las
/// copias (mark_receipt_copy); aquí se traduce el data congelado al contrato, se enmascara y se registra cada emisión.
/// </summary>
public class ReceiptService(
    IOperationRepository operations, IReceiptRepository receipts, ISimulatedInbox inbox, IUnitOfWork unitOfWork,
    ICurrentUser currentUser, TimeProvider time) : IReceiptService
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>issue_receipt guarda la fecha en hora de Bogotá, sin zona.</summary>
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    public async Task<ReceiptDto> GetAsync(Guid operationId, ReceiptViewChannel channel, CancellationToken ct = default)
    {
        var receipt = await GetReceiptAsync(operationId, ct);
        // InTransactionAsync guarda al final: is_copy vuelve calculado por el trigger.
        var issue = await unitOfWork.InTransactionAsync(_ => Task.FromResult(Issue(receipt, ToChannel(channel))), ct);
        return new ReceiptDto(receipt.Code, receipt.OperationId, receipt.ContentHash, issue.IsCopy, receipt.CreatedAt,
            ToData(Read(receipt)));
    }

    public async Task<ReceiptDeliveryDto> SendAsync(Guid operationId, SendReceiptRequest request,
        CancellationToken ct = default)
    {
        var receipt = await GetReceiptAsync(operationId, ct);
        var data = Read(receipt);
        var channel = request.Channel!.Value;
        return await unitOfWork.InTransactionAsync(async tx =>
        {
            var whatsApp = channel == ReceiptSendChannel.WhatsApp;
            var issue = Issue(receipt, whatsApp ? ReceiptChannel.WhatsApp : ReceiptChannel.Sms);
            // El trigger decide si es copia: se guarda la emisión antes de escribir el mensaje.
            await unitOfWork.SaveChangesAsync(tx);
            var message = inbox.Send(
                whatsApp ? MessageChannel.WhatsApp : MessageChannel.Sms,
                MessageKind.Receipt, data.User.Mobile, Content(receipt, data, issue.IsCopy), operationId: operationId);
            return new ReceiptDeliveryDto(channel, Masking.Phone(data.User.Mobile), issue.IsCopy, message.CreatedAt);
        }, ct);
    }

    /// <summary>
    /// 404 si la operación no existe; 403 a otro corresponsal; 409 receipt_not_available en cargas de cupo y
    /// 409 operation_not_completed si aún no está completada.
    /// </summary>
    private async Task<Receipt> GetReceiptAsync(Guid operationId, CancellationToken ct)
    {
        var operation = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanReadAgent(operation.AgentId);
        if (operation.Kind == OperationKind.QuotaTopUp) throw ApiErrors.ReceiptNotAvailable();
        if (operation.State != OperationState.Completed) throw ApiErrors.OperationNotCompleted();
        // El trigger issue_receipt_on_completion lo crea al completar: si falta, la base está inconsistente (500).
        return await receipts.GetByOperationAsync(operationId, ct)
               ?? throw new InvalidOperationException($"La operación {operation.Reference} está completada sin recibo");
    }

    /// <summary>
    /// Emisión a nombre del corresponsal. El admin no tiene corresponsal: su lectura queda como system
    /// (ck_issuance_requester exige agent_id con agent).
    /// </summary>
    private ReceiptIssue Issue(Receipt receipt, ReceiptChannel channel)
    {
        var issue = new ReceiptIssue
        {
            ReceiptId = receipt.Id,
            Channel = channel,
            RequestedBy = currentUser.AgentId is null ? ReceiptRequester.System : ReceiptRequester.Agent,
            AgentId = currentUser.AgentId,
            CreatedAt = time.GetUtcNow(),
        };
        receipts.AddIssue(issue);
        return issue;
    }

    private static ReceiptChannel ToChannel(ReceiptViewChannel channel) => channel switch
    {
        ReceiptViewChannel.Print => ReceiptChannel.Print,
        ReceiptViewChannel.Pdf => ReceiptChannel.Pdf,
        _ => ReceiptChannel.Screen,
    };

    private static FrozenReceipt Read(Receipt receipt) =>
        receipt.Data.Deserialize<FrozenReceipt>()
        ?? throw new InvalidOperationException($"Recibo {receipt.Code} sin contenido");

    private static ReceiptDataDto ToData(FrozenReceipt d)
    {
        var local = DateTime.ParseExact(d.Date, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var date = new DateTimeOffset(local, Bogota.GetUtcOffset(local));
        return new ReceiptDataDto(
            d.Number,
            d.Type switch
            {
                "deposit" => CashMovingOperationType.Deposit,
                "withdrawal" => CashMovingOperationType.Withdrawal,
                _ => throw new InvalidOperationException($"Recibo {d.Number} con tipo {d.Type}"),
            },
            date,
            d.Currency,
            new ReceiptMerchantDto(d.Merchant.Name, d.Merchant.Address, d.Merchant.Phone),
            new OperationCustomerDto(Masking.Phone(d.User.Mobile), Masking.WalletAddress(d.User.PaymentPointer)),
            new ReceiptAmountsDto(d.Amounts.RequestedAmount, d.Amounts.RoundValue, d.Amounts.Commission,
                d.Amounts.NetAmount, d.Amounts.ChargedAmount),
            new ReceiptPaymentDto(
                d.Payment.Rail switch
                {
                    "open_payments" => PaymentRailName.OpenPayments,
                    "bre_b_simulated" => PaymentRailName.BreBSimulated,
                    _ => throw new InvalidOperationException($"Recibo {d.Number} con riel {d.Payment.Rail}"),
                },
                d.Payment.Reference
                ?? throw new InvalidOperationException($"Recibo {d.Number} sin referencia del riel")));
    }

    private static string Content(Receipt receipt, FrozenReceipt d, bool isCopy)
    {
        var what = d.Type == "withdrawal"
            ? $"Retiro de {d.Amounts.NetAmount.ToString("C0", Colombia)} en efectivo"
            : $"Depósito de {d.Amounts.NetAmount.ToString("C0", Colombia)} a tu billetera";
        return $"Relia: recibo {d.Number}{(isCopy ? " (COPIA)" : "")}. {what} en {d.Merchant.Name}. " +
               $"Monto cobrado {d.Amounts.ChargedAmount.ToString("C0", Colombia)}, comisión " +
               $"{d.Amounts.Commission.ToString("C0", Colombia)}. Código {receipt.Code}.";
    }

    // Forma del data que congela issue_receipt (snake_case, datos completos).
    private record FrozenReceipt(
        [property: JsonPropertyName("number")] string Number,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("merchant")] FrozenMerchant Merchant,
        [property: JsonPropertyName("user")] FrozenUser User,
        [property: JsonPropertyName("amounts")] FrozenAmounts Amounts,
        [property: JsonPropertyName("payment")] FrozenPayment Payment);

    private record FrozenMerchant(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("phone")] string Phone);

    private record FrozenUser(
        [property: JsonPropertyName("mobile")] string Mobile,
        [property: JsonPropertyName("payment_pointer")] string PaymentPointer);

    private record FrozenAmounts(
        [property: JsonPropertyName("requested_amount")] long RequestedAmount,
        [property: JsonPropertyName("round_value")] bool RoundValue,
        [property: JsonPropertyName("commission")] long Commission,
        [property: JsonPropertyName("net_amount")] long NetAmount,
        [property: JsonPropertyName("charged_amount")] long ChargedAmount);

    private record FrozenPayment(
        [property: JsonPropertyName("rail")] string Rail,
        [property: JsonPropertyName("reference")] string? Reference);
}
