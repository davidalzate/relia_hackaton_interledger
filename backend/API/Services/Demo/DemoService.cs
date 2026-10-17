using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;
using Relia.Api.Services.Operations;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services.Demo;

/// <summary>Ticket 16 y simuladores del Modo demo.</summary>
public interface IDemoService
{
    /// <summary>Cancela cobros pendientes en el riel, vacía los datos, vuelve a sembrar y apaga los fallos.</summary>
    Task ResetAsync(CancellationToken ct = default);

    Task<DemoMessagePageDto> ListMessagesAsync(string? phone, string? cursor, int limit, CancellationToken ct = default);

    Task MarkReadAsync(string? phone, CancellationToken ct = default);

    DemoFaultsDto GetFaults();

    DemoFaultsDto SetFaults(DemoFaultsDto faults);

    /// <summary>"La solicitud vence ya": cancela en el riel y deja el retiro o la carga expired.</summary>
    Task<OperationDto> ExpireNowAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Simula que llegó el pago Bre-B de una carga pendiente: la completa y sube el cupo.</summary>
    Task<OperationDto> SimulateBreBPaymentAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>"Mi billetera": retiros y cargas por Wallet pendientes de aprobación para ese celular.</summary>
    Task<DemoPaymentRequestListDto> ListPaymentRequestsAsync(string phone, CancellationToken ct = default);
}

public class DemoService(
    IDemoDataRepository data, DemoSeeder seeder, DemoState state, ISimulatedMessageRepository messages,
    IUnitOfWork unitOfWork, IdempotencyStore idempotency, RateLimiter rateLimiter, IOperationRepository operations,
    OperationTracker tracker, OperationView view) : IDemoService
{
    public DemoFaultsDto GetFaults() => new(state.FailNextPayment);

    public DemoFaultsDto SetFaults(DemoFaultsDto faults)
    {
        state.FailNextPayment = faults.FailNextPayment;
        return GetFaults();
    }

    public async Task<OperationDto> ExpireNowAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        await tracker.RefreshAsync(op, ct);
        if (op.Type == Db.OperationType.Deposit || op.Status != Db.OperationStatus.Pending)
            throw NotPending("Solo retiros y cargas pendientes");

        switch (await tracker.CancelPendingAsync(op, Db.OperationStatus.Expired, "Vencida desde el Modo demo",
                    Db.EventSource.Simulator, ct))
        {
            case OperationTracker.CancelResult.NotCancellable:
                throw NotPending("El pagador ya aprobó: la solicitud sigue su curso");
            case OperationTracker.CancelResult.RailUnavailable:
                throw new Rail.RailUnavailableException("No se pudo cancelar en el riel");
        }
        return await view.BuildAsync(op, ct);
    }

    public async Task<OperationDto> SimulateBreBPaymentAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        await tracker.RefreshAsync(op, ct);
        if (op is not { Type: Db.OperationType.FloatLoad, LoadMethod: Db.LoadMethod.BreB, Status: Db.OperationStatus.Pending })
            throw NotPending("No es una carga Bre-B pendiente");

        await tracker.CompleteAsync(op, op.LatestPayment()!, Db.EventSource.Simulator, ct);
        return await view.BuildAsync(op, ct);
    }

    /// <summary>
    /// El contrato no define un code para "la operación ya no está pendiente" en el Modo demo; se usa el más
    /// cercano del enum.
    /// </summary>
    private static ApiException NotPending(string title) => ApiErrors.Conflict("operation_not_cancellable", title);

    public async Task<DemoPaymentRequestListDto> ListPaymentRequestsAsync(string phone, CancellationToken ct = default)
    {
        var items = new List<DemoPaymentRequestDto>();
        foreach (var op in await operations.ListPendingAsync(ct))
        {
            var payer = op.Type switch
            {
                Db.OperationType.Withdrawal => op.WalletKey?.Mobile,
                Db.OperationType.FloatLoad when op.LoadMethod == Db.LoadMethod.Wallet => op.Agent?.Phone,
                _ => null,
            };
            if (payer != phone) continue;

            await tracker.RefreshAsync(op, ct);
            if (op.Status != Db.OperationStatus.Pending || op.LatestPayment() is not { ApprovalUrl: { } url } payment)
                continue;
            items.Add(new DemoPaymentRequestDto(op.Id, op.Reference(), op.Type.ToApi(), op.Agent!.Name,
                op.ChargedAmount, Clock.Utc(payment.ExpiresAt!.Value), url));
        }
        return new DemoPaymentRequestListDto(items);
    }

    public async Task<DemoMessagePageDto> ListMessagesAsync(string? phone, string? cursor, int limit,
        CancellationToken ct = default)
    {
        long? before = null;
        if (cursor is not null)
            before = long.TryParse(cursor, out var id) ? id : throw ApiErrors.Validation("cursor", "Cursor inválido");

        var items = await messages.ListAsync(phone, before, limit, ct);
        var unread = await messages.CountUnreadAsync(phone, ct);
        return new DemoMessagePageDto(items.Select(Messenger.ToDto).ToList(), unread,
            items.Count == limit ? items[^1].Id.ToString() : null);
    }

    public async Task MarkReadAsync(string? phone, CancellationToken ct = default)
    {
        await messages.MarkReadAsync(phone, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        // Los cobros pendientes se cancelan en el riel; el dinero ya movido en billeteras reales no se devuelve.
        foreach (var op in await operations.ListPendingAsync(ct))
            if (op.LatestPayment() is { Direction: Db.PaymentDirection.Incoming } payment)
                await tracker.TryCancelAtRailAsync(payment, ct);

        await data.WipeAsync(ct);
        await seeder.SeedAsync(ct);
        state.FailNextPayment = false;
        idempotency.Clear();
        rateLimiter.Clear();
    }
}

/// <summary>Estado del Modo demo en memoria del proceso (no sobrevive a un reinicio).</summary>
public class DemoState
{
    private int _failNextPayment;

    public bool FailNextPayment
    {
        get => Volatile.Read(ref _failNextPayment) == 1;
        set => Volatile.Write(ref _failNextPayment, value ? 1 : 0);
    }

    /// <summary>Consume el fallo forzado: true solo una vez.</summary>
    public bool TryConsumeFailure() => Interlocked.Exchange(ref _failNextPayment, 0) == 1;
}
