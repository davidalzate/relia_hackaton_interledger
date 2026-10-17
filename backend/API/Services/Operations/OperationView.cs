using Relia.Api.Contracts;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Operations;

/// <summary>Arma el Operation del contrato: montos, pago de riel, saldos antes/después y datos enmascarados.</summary>
public class OperationView(IAgentRepository agents, ILedgerRepository ledger, IOperationRepository operations)
{
    /// <summary>Evento de auditoría de "confirmar entrega del efectivo" (no cambia el estado).</summary>
    public const string CashHandover = "cash_handover";

    public async Task<OperationDto> BuildAsync(Db.Operation o, CancellationToken ct = default)
    {
        var payment = o.LatestPayment();
        var pending = o.Status == Db.OperationStatus.Pending;

        BalanceChangeDto balances;
        if (o.Status == Db.OperationStatus.Completed && await ledger.GetByOperationAsync(o.Id, ct) is { } entry)
            balances = entry.ToChange();
        else
        {
            var agent = await agents.GetByIdAsync(o.AgentId, ct);
            balances = (agent?.AgentBalance ?? new Db.AgentBalance()).Unchanged();
        }

        DateTimeOffset? handedOverAt = null;
        if (o.Type == Db.OperationType.Withdrawal && o.Status == Db.OperationStatus.Completed)
            handedOverAt = Clock.Utc((await operations.ListEventsAsync(o.Id, ct))
                .FirstOrDefault(e => e.Details == CashHandover)?.CreatedAt);

        return new OperationDto(
            o.Id, o.Reference(), o.AgentId, o.Type.ToApi(), o.Status.ToApi(),
            Mapping.ToFailureCode(payment?.ErrorCode), o.Status == Db.OperationStatus.Completed ? null : payment?.ErrorMessage,
            Clock.Utc(o.CreatedAt), Clock.Utc(o.CompletedAt), Clock.Utc(payment?.ExpiresAt),
            o.RequestedAmount, o.RoundValue, new FeeDto(o.TotalCommission, o.ReliaCommission, o.AgentCommission),
            o.NetAmount, o.ChargedAmount,
            o.WalletKey is { } key ? new OperationCustomerDto(Masking.Phone(key.Mobile), Masking.Wallet(key.PaymentPointer)) : null,
            o.LoadMethod?.ToApi(),
            pending ? payment?.ApprovalUrl : null,
            pending && o.LoadMethod == Db.LoadMethod.BreB ? QrPayload(o, payment) : null,
            handedOverAt, balances);
    }

    /// <summary>QR Bre-B simulado: referencia, monto y vencimiento.</summary>
    public static string QrPayload(Db.Operation o, Db.Payment? payment) =>
        $"breb://pay?ref={o.Reference()}&amount={o.ChargedAmount}&expires={Clock.Utc(payment?.ExpiresAt):O}";
}
