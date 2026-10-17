using Microsoft.Extensions.Options;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Rail;
using Relia.Api.Repositories;
using Relia.Api.Services.Demo;
using Relia.Api.Services.Operations;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services;

/// <summary>Tickets 06 (desglose), 09 (depósito), 11 (retiro) y 12 (carga de cupo).</summary>
public class OperationService(
    IAgentRepository agents, IFeeRepository fees, IWalletKeyRepository keys, IOperationRepository operations,
    IPaymentRepository payments, IUnitOfWork unitOfWork, IRailClient rail, OperationTracker tracker, Messenger messenger,
    OperationView view, IdempotencyStore idempotency, DemoState demo, IOptions<ReliaOptions> relia,
    TimeProvider time, ICurrentUser currentUser, AgentLocks locks, ILogger<OperationService> log) : IOperationService
{
    private static readonly System.Globalization.CultureInfo Co = System.Globalization.CultureInfo.GetCultureInfo("es-CO");

    public async Task<OperationPreviewDto> PreviewAsync(Guid agentId, OperationPreviewRequest request,
        CancellationToken ct = default)
    {
        currentUser.EnsureCanOperate(agentId);
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        var balance = agent.AgentBalance!;
        var reserved = await operations.GetReservationsAsync(agentId, ct);
        var type = request.Type.ToDb();
        var breakdown = Breakdown.Of(request.RequestedAmount, request.RoundAmount, await fees.GetCurrentAsync(ct));
        var (floatChange, commissionChange, cashChange) = breakdown.Changes(type);

        return new OperationPreviewDto(request.Type, request.RequestedAmount, request.RoundAmount, breakdown.FeeDto,
            breakdown.Net, breakdown.Charged,
            balance.FloatBalance - reserved.FloatReserved + floatChange,
            balance.ExpectedCash - reserved.CashReserved + cashChange,
            balance.AccruedCommission + commissionChange,
            breakdown.Violations(type, balance, reserved));
    }

    public Task<OperationDto> CreateDepositAsync(Guid agentId, string idempotencyKey, CreateDepositRequest request,
        CancellationToken ct = default)
    {
        currentUser.EnsureCanOperate(agentId);
        if (!request.CashReceivedConfirmed)
            throw ApiErrors.Validation("cashReceivedConfirmed", "El corresponsal debe confirmar que recibió el efectivo");

        return CreateOnceAsync(agentId, "deposits", idempotencyKey, request, async () =>
        {
            var key = await keys.GetByMobileAsync(request.Phone, ct) ?? throw KeyService.KeyNotFound();
            var breakdown = await BreakdownAsync(request.RequestedAmount, request.RoundAmount, ct);

            return await CreateAsync(agentId, idempotencyKey, Db.OperationType.Deposit, breakdown, key, null,
                Db.PaymentDirection.Outgoing, relia.Value.WalletAddress, key.PaymentPointer, breakdown.Net,
                expiresIn: null, ct);
        });
    }

    public Task<OperationDto> CreateWithdrawalAsync(Guid agentId, string idempotencyKey,
        CreateWithdrawalRequest request, CancellationToken ct = default)
    {
        currentUser.EnsureCanOperate(agentId);
        return CreateOnceAsync(agentId, "withdrawals", idempotencyKey, request, async () =>
        {
            var key = await keys.GetByMobileAsync(request.Phone, ct) ?? throw KeyService.KeyNotFound();
            var breakdown = await BreakdownAsync(request.RequestedAmount, request.RoundAmount, ct);

            return await CreateAsync(agentId, idempotencyKey, Db.OperationType.Withdrawal, breakdown, key, null,
                Db.PaymentDirection.Incoming, key.PaymentPointer, relia.Value.WalletAddress, breakdown.Charged,
                ApprovalWindow, ct, notifyPhone: key.Mobile);
        });
    }

    /// <summary>
    /// Bre-B: QR simulado de 10 minutos, sin riel; el pago se marca recibido desde el Modo demo.
    /// Wallet: cobro a la billetera registrada del corresponsal, con aprobación y 5 minutos. Sin comisión.
    /// </summary>
    public Task<OperationDto> CreateQuotaTopUpAsync(Guid agentId, string idempotencyKey,
        CreateQuotaTopUpRequest request, CancellationToken ct = default)
    {
        currentUser.EnsureCanOperate(agentId);
        return CreateOnceAsync(agentId, "quota-top-ups", idempotencyKey, request, async () =>
        {
            var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
            var amount = request.RequestedAmount;
            var breakdown = new Breakdown(amount, false, 0, 0, 0, amount, amount);
            var breB = request.Method == TopUpMethod.BreB;

            return await CreateAsync(agentId, idempotencyKey, Db.OperationType.FloatLoad, breakdown, null,
                request.Method.ToDb(), Db.PaymentDirection.Incoming, agent.PaymentPointer, relia.Value.WalletAddress,
                amount, breB ? BreBWindow : ApprovalWindow, ct, notifyPhone: breB ? null : agent.Phone);
        });
    }

    /// <summary>Vigencia del QR Bre-B simulado.</summary>
    private static readonly TimeSpan BreBWindow = TimeSpan.FromMinutes(10);

    public async Task<OperationDto> GetAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await ReadableAsync(operationId, ct);
        await tracker.RefreshAsync(op, ct);
        return await view.BuildAsync(op, ct);
    }

    /// <summary>Solo retiros y cargas pending, antes de que el pagador apruebe.</summary>
    public async Task<OperationDto> CancelAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await ReadableAsync(operationId, ct);
        currentUser.EnsureCanOperate(op.AgentId);
        await tracker.RefreshAsync(op, ct);
        if (op.Type == Db.OperationType.Deposit || op.Status != Db.OperationStatus.Pending)
            throw NotCancellable();

        switch (await tracker.CancelPendingAsync(op, Db.OperationStatus.Cancelled, "Cancelada por el corresponsal",
                    Db.EventSource.Agent, ct))
        {
            case OperationTracker.CancelResult.NotCancellable:
                throw NotCancellable();
            case OperationTracker.CancelResult.RailUnavailable:
                throw new RailUnavailableException("No se pudo cancelar en el riel");
        }
        return await view.BuildAsync(op, ct);
    }

    /// <summary>Solo retiros completed. Deja un evento de auditoría; no cambia estado ni saldos. Idempotente.</summary>
    public async Task<OperationDto> ConfirmCashHandoverAsync(Guid operationId, CancellationToken ct = default)
    {
        var op = await ReadableAsync(operationId, ct);
        currentUser.EnsureCanOperate(op.AgentId);
        await tracker.RefreshAsync(op, ct);
        if (op.Type != Db.OperationType.Withdrawal || op.Status != Db.OperationStatus.Completed)
            throw ApiErrors.Conflict("operation_not_completed", "El retiro no está completado");

        var events = await operations.ListEventsAsync(op.Id, ct);
        if (events.All(e => e.Details != OperationView.CashHandover))
        {
            operations.AddEvent(new Db.OperationEvent
            {
                OperationId = op.Id, PreviousStatus = Db.OperationStatus.Completed,
                NewStatus = Db.OperationStatus.Completed, Source = Db.EventSource.Agent,
                Details = OperationView.CashHandover, CreatedAt = time.UtcNow(),
            });
            await unitOfWork.SaveChangesAsync(ct);
        }
        return await view.BuildAsync(op, ct);
    }

    private static ApiException NotCancellable() =>
        ApiErrors.Conflict("operation_not_cancellable", "La solicitud ya no se puede cancelar");

    /// <summary>Plazo para aprobar un retiro o una carga por Wallet.</summary>
    private static readonly TimeSpan ApprovalWindow = TimeSpan.FromMinutes(5);

    // ---- Piezas comunes ----

    private async Task<Db.Operation> ReadableAsync(Guid operationId, CancellationToken ct)
    {
        var op = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanRead(op.AgentId);
        return op;
    }

    private async Task<Breakdown> BreakdownAsync(long requested, bool round, CancellationToken ct)
    {
        var breakdown = Breakdown.Of(requested, round, await fees.GetCurrentAsync(ct));
        if (breakdown.TooLow)
            throw ApiErrors.Unprocessable("amount_too_low", "El monto debe ser mayor a la comisión");
        return breakdown;
    }

    /// <summary>
    /// Idempotencia en dos niveles: en memoria (respuesta exacta) y en la base (operations tiene
    /// UNIQUE(agent_id, idempotency_key)), que sobrevive a un reinicio.
    /// </summary>
    private Task<OperationDto> CreateOnceAsync(Guid agentId, string route, string idempotencyKey, object request,
        Func<Task<OperationDto>> create) =>
        idempotency.RunAsync($"{agentId}|{route}", idempotencyKey, request, async () =>
        {
            using var _ = await locks.AcquireAsync(agentId);
            if (await operations.GetByIdempotencyKeyAsync(agentId, idempotencyKey) is { } existing)
            {
                var full = await operations.GetByIdAsync(existing.Id) ?? existing;
                return await view.BuildAsync(full);
            }
            return await create();
        });

    /// <summary>
    /// Crea la operación y su pago en tres pasos, con el candado del corresponsal tomado (CreateOnceAsync):
    /// verifica saldos, pide el pago al riel fuera de toda transacción y guarda en una transacción corta que
    /// vuelve a verificar bajo FOR UPDATE. Si el riel no lo acepta (503) no se ha escrito nada. El id del pago
    /// sale de la clave de idempotencia, así que un reintento recupera el pago del riel en vez de duplicarlo.
    /// Con el fallo forzado del Modo demo, la operación nace failed sin riel.
    /// </summary>
    private async Task<OperationDto> CreateAsync(Guid agentId, string idempotencyKey, Db.OperationType type,
        Breakdown breakdown, Db.WalletKey? key, Db.LoadMethod? loadMethod, Db.PaymentDirection direction,
        string source, string destination, long railAmount, TimeSpan? expiresIn, CancellationToken ct,
        string? notifyPhone = null)
    {
        var now = time.UtcNow();
        var op = new Db.Operation
        {
            Id = Guid.NewGuid(), AgentId = agentId, Type = type, LoadMethod = loadMethod, WalletKeyId = key?.Id,
            RequestedAmount = breakdown.Requested, RoundValue = breakdown.Round,
            TotalCommission = breakdown.FeeTotal, ReliaCommission = breakdown.FeeRelia, AgentCommission = breakdown.FeeAgent,
            NetAmount = breakdown.Net, ChargedAmount = breakdown.Charged, Status = Db.OperationStatus.Pending,
            IdempotencyKey = idempotencyKey, CreatedAt = now, UpdatedAt = now,
        };
        if (type != Db.OperationType.FloatLoad) op.FeeId = (await fees.GetCurrentAsync(ct)).Id;

        var payment = new Db.Payment
        {
            Id = PaymentLog.PaymentId($"{agentId}|operation", idempotencyKey), OperationId = op.Id,
            Rail = loadMethod == Db.LoadMethod.BreB ? Db.PaymentRail.BreBSimulated : Db.PaymentRail.OpenPayments,
            Direction = direction, Status = Db.OperationStatus.Pending, Amount = railAmount, Currency = "COP",
            SourcePaymentPointer = source, DestinationPaymentPointer = destination, RailDetails = "{}",
            ExpiresAt = expiresIn is null ? null : now + expiresIn.Value, CreatedAt = now, UpdatedAt = now,
        };

        // 1. Verificación previa: falla rápido sin llamar al riel.
        await unitOfWork.InTransactionAsync(async tx =>
        {
            var balance = await agents.GetBalanceForUpdateAsync(agentId, tx);
            EnsureAvailable(type, breakdown, balance, await operations.GetReservationsAsync(agentId, tx));
            return true;
        }, ct);

        // 2. El riel, sin transacción abierta. Un 503 sale de aquí sin haber escrito nada.
        var forcedFailure = demo.TryConsumeFailure();
        RailPayment? railPayment = null;
        if (!forcedFailure && payment.Rail == Db.PaymentRail.OpenPayments)
        {
            railPayment = await rail.CreateOrUnavailableAsync(new CreateRailPayment(payment.Id,
                direction == Db.PaymentDirection.Outgoing ? RailDirection.Outgoing : RailDirection.Incoming,
                direction == Db.PaymentDirection.Outgoing ? destination : source, railAmount,
                payment.ExpiresAt is null ? null : Clock.Utc(payment.ExpiresAt.Value),
                await DescriptionAsync(type, agentId, ct)), log, ct);
            OperationTracker.Record(payment, railPayment, now);
            PaymentLog.RailResponse(payment, "created", railPayment, now);
        }

        // 3. Guardar, verificando otra vez: un ajuste de efectivo pudo cambiar los saldos mientras tanto.
        try
        {
            await unitOfWork.InTransactionAsync(async tx =>
            {
                var balance = await agents.GetBalanceForUpdateAsync(agentId, tx);
                EnsureAvailable(type, breakdown, balance, await operations.GetReservationsAsync(agentId, tx));

                operations.Add(op);
                payments.Add(payment);
                operations.AddEvent(new Db.OperationEvent
                {
                    OperationId = op.Id, NewStatus = Db.OperationStatus.Pending, Source = Db.EventSource.Agent,
                    Details = "created", CreatedAt = now,
                });
                return true;
            }, ct);
        }
        catch (Exception e) when (railPayment is not null)
        {
            await AbandonAtRailAsync(payment, railPayment, e);
            throw;
        }

        var created = await operations.GetByIdAsync(op.Id, ct) ?? op;
        if (forcedFailure)
            await tracker.FinishAsync(created, payment, Db.OperationStatus.Failed, Mapping.PaymentFailed,
                "Fallo forzado desde el Modo demo", Db.EventSource.Simulator, ct);
        else if (notifyPhone is not null && payment.ApprovalUrl is not null)
        {
            // El pagador recibe la solicitud por SMS simulado y aprueba en su billetera.
            messenger.Sms(notifyPhone, Db.MessageType.PaymentRequest,
                $"Relia: {created.Agent?.Name} te pide ${breakdown.Charged.ToString("N0", Co)} " +
                $"({Label(type)} {created.Reference()}). Aprueba en tu billetera antes de 5 minutos.",
                payment.ApprovalUrl, created.Id);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return await view.BuildAsync(created, ct);
    }

    /// <summary>
    /// El riel aceptó pero la base no guardó la operación. Un cobro (incoming) se cancela; un envío (outgoing)
    /// ya está en camino y no se puede: queda en el log para conciliar, y reintentar con la misma clave lo
    /// recupera porque el riel devuelve el mismo pago.
    /// </summary>
    private async Task AbandonAtRailAsync(Db.Payment payment, RailPayment railPayment, Exception cause)
    {
        if (railPayment.Direction == RailDirection.Incoming)
        {
            try
            {
                await rail.CancelPaymentAsync(railPayment.Id);
                log.LogError(cause, "No se guardó la operación del pago {PaymentId}; se canceló en el riel", payment.Id);
                return;
            }
            catch (Exception cancel)
            {
                log.LogError(cancel, "No se pudo cancelar en el riel el pago {PaymentId}", payment.Id);
            }
        }
        log.LogCritical(cause,
            "El riel aceptó el pago {PaymentId} ({RailPaymentId}) pero la base no lo guardó: conciliar o reintentar con la misma clave",
            payment.Id, railPayment.Id);
    }

    private static void EnsureAvailable(Db.OperationType type, Breakdown breakdown, Db.AgentBalance balance,
        Reservations reserved)
    {
        if (type == Db.OperationType.Deposit && balance.FloatBalance - reserved.FloatReserved < breakdown.Charged)
            throw ApiErrors.Shortfall("insufficient_quota", "Cupo insuficiente",
                breakdown.Charged - (balance.FloatBalance - reserved.FloatReserved));
        if (type == Db.OperationType.Withdrawal && balance.ExpectedCash - reserved.CashReserved < breakdown.Net)
            throw ApiErrors.Shortfall("insufficient_cash", "Efectivo insuficiente",
                breakdown.Net - (balance.ExpectedCash - reserved.CashReserved));
    }

    /// <summary>Texto que ve el pagador en su billetera (máx. 140). El número OP-… aún no existe: lo asigna la base al guardar.</summary>
    private async Task<string> DescriptionAsync(Db.OperationType type, Guid agentId, CancellationToken ct)
    {
        var agent = await agents.GetByIdAsync(agentId, ct);
        var text = $"{Label(type)} · {agent?.Name}";
        return text.Length <= 140 ? text : text[..140];
    }

    private static string Label(Db.OperationType type) => type switch
    {
        Db.OperationType.Deposit => "Depósito",
        Db.OperationType.Withdrawal => "Retiro",
        _ => "Carga de cupo",
    };
}
