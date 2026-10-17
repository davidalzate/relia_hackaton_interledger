using System.Globalization;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;
using Relia.Api.Services.Operations;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services;

/// <summary>
/// Ticket 10: recibos congelados de depósitos y retiros completados. Cada vista o envío registra una emisión;
/// desde la segunda, isCopy es true.
/// </summary>
public class ReceiptService(
    IOperationRepository operations, IReceiptRepository receipts, IAgentRepository agents, OperationTracker tracker,
    Messenger messenger, IUnitOfWork unitOfWork, TimeProvider time, ICurrentUser currentUser) : IReceiptService
{
    private static readonly CultureInfo Co = CultureInfo.GetCultureInfo("es-CO");

    public async Task<ReceiptDto> GetAsync(Guid operationId, ReceiptViewChannel channel, CancellationToken ct = default)
    {
        var (op, receipt) = await ReceiptOfAsync(operationId, ct);
        var isCopy = await IssueAsync(receipt, op, channel switch
        {
            ReceiptViewChannel.Print => Db.ReceiptIssueChannel.Print,
            ReceiptViewChannel.Pdf => Db.ReceiptIssueChannel.Pdf,
            _ => Db.ReceiptIssueChannel.Screen,
        }, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new ReceiptDto(receipt.Code, op.Id, receipt.ContentHash, isCopy, Clock.Utc(receipt.CreatedAt),
            await DataAsync(op, receipt, ct));
    }

    public async Task<ReceiptDeliveryDto> SendAsync(Guid operationId, SendReceiptRequest request,
        CancellationToken ct = default)
    {
        var (op, receipt) = await ReceiptOfAsync(operationId, ct);
        var phone = op.WalletKey!.Mobile;
        var whatsapp = request.Channel == ReceiptSendChannel.WhatsApp;
        var isCopy = await IssueAsync(receipt, op,
            whatsapp ? Db.ReceiptIssueChannel.Whatsapp : Db.ReceiptIssueChannel.Sms, ct);

        messenger.Send(whatsapp ? Db.MessageChannel.Whatsapp : Db.MessageChannel.Sms, phone, Db.MessageType.Receipt,
            $"Relia: comprobante {op.Reference()} ({(op.Type == Db.OperationType.Deposit ? "depósito" : "retiro")}) " +
            $"por ${op.RequestedAmount.ToString("N0", Co)}. Código {receipt.Code}.{(isCopy ? " COPIA." : "")}",
            null, op.Id);
        await unitOfWork.SaveChangesAsync(ct);

        return new ReceiptDeliveryDto(request.Channel, Masking.Phone(phone), isCopy, time.GetUtcNow());
    }

    private async Task<(Db.Operation, Db.Receipt)> ReceiptOfAsync(Guid operationId, CancellationToken ct)
    {
        var op = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanRead(op.AgentId);
        await tracker.RefreshAsync(op, ct);

        if (op.Type == Db.OperationType.FloatLoad)
            throw ApiErrors.Conflict("receipt_not_available", "Las cargas de cupo no tienen recibo");
        if (op.Status != Db.OperationStatus.Completed)
            throw ApiErrors.Conflict("operation_not_completed", "La operación no está completada");

        var receipt = await receipts.GetByOperationAsync(op.Id, ct)
                      ?? throw new InvalidOperationException($"La operación {op.Reference()} completada no tiene recibo");
        return (op, receipt);
    }

    /// <summary>Registra la emisión. En Postgres el trigger también calcula es_copia, con el mismo criterio.</summary>
    private async Task<bool> IssueAsync(Db.Receipt receipt, Db.Operation op, Db.ReceiptIssueChannel channel,
        CancellationToken ct)
    {
        var isCopy = await receipts.CountIssuancesAsync(receipt.Id, ct) > 0;
        var byAgent = currentUser.Role == Role.Agent;
        receipts.AddIssuance(new Db.ReceiptIssuance
        {
            ReceiptId = receipt.Id, Channel = channel,
            RequestedBy = byAgent ? Db.ReceiptRequester.Agent : Db.ReceiptRequester.System,
            AgentId = byAgent ? op.AgentId : null, IsCopy = isCopy, CreatedAt = time.UtcNow(),
        });
        return isCopy;
    }

    private async Task<ReceiptDataDto> DataAsync(Db.Operation op, Db.Receipt receipt, CancellationToken ct)
    {
        var snapshot = ReceiptSnapshot.TryRead(receipt.Data);
        if (snapshot is null)
        {
            // Recibo congelado por el trigger de Postgres, con otro formato: se arma con la operación, que es inmutable.
            var agent = op.Agent ?? await agents.GetByIdAsync(op.AgentId, ct);
            var payment = op.Payments.Single(p => p.Status == Db.OperationStatus.Completed);
            snapshot = ReceiptSnapshot.Of(op, agent!, op.WalletKey!, payment, op.CompletedAt ?? receipt.CreatedAt);
        }

        return new ReceiptDataDto(snapshot.Reference,
            snapshot.Type == "deposit" ? CashMovingOperationType.Deposit : CashMovingOperationType.Withdrawal,
            snapshot.Date, snapshot.Currency,
            new ReceiptMerchantDto(snapshot.MerchantName, snapshot.MerchantAddress, snapshot.MerchantPhone),
            new OperationCustomerDto(Masking.Phone(snapshot.CustomerPhone), Masking.Wallet(snapshot.CustomerWallet)),
            new ReceiptAmountsDto(snapshot.RequestedAmount, snapshot.RoundAmount, snapshot.Fee, snapshot.NetAmount,
                snapshot.ChargedAmount),
            new ReceiptPaymentDto(snapshot.Rail == "openPayments" ? PaymentRailName.OpenPayments : PaymentRailName.BreBSimulated,
                snapshot.RailReference));
    }
}
