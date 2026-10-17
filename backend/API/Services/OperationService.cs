using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Relia.Api.Auth;
using Relia.Api.Configuration;
using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Rail;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>
/// Desglose, depósitos, retiros, cargas de cupo y acciones sobre una operación.
/// La idempotencia de las creaciones la resuelve el filtro [Idempotent] del controlador, que abre la
/// transacción: un 4xx o un 503 deshacen todo, incluido lo que se escribió antes de llamar al riel.
/// </summary>
public class OperationService(
    IAgentRepository agents, IFeeRepository fees, IWalletKeyRepository keys, IOperationRepository operations,
    IPaymentRepository payments, ILedgerRepository ledgerEntries, ILedger ledger, IRailClient rail,
    IOperationSync sync, ISimulatedInbox inbox, IUnitOfWork unitOfWork, ICurrentUser currentUser,
    IOptions<ReliaOptions> options, TimeProvider time, ILogger<OperationService> logger) : IOperationService
{
    /// <summary>Tope de la descripción que ve el pagador en su billetera (rail-proxy.openapi.yaml).</summary>
    private const int MaxRailDescription = 140;

    /// <summary>Plazo para aprobar un cobro por Wallet (retiro y carga).</summary>
    private static readonly TimeSpan WalletApprovalWindow = TimeSpan.FromMinutes(5);

    /// <summary>Vigencia del QR Bre-B simulado de una carga de cupo.</summary>
    private static readonly TimeSpan BreBQrWindow = TimeSpan.FromMinutes(10);

    /// <summary>Formato de pesos en los textos que ve el usuario ("$ 100.000").</summary>
    private static readonly CultureInfo PesoFormat = CultureInfo.GetCultureInfo("es-CO");

    /// <summary>Siempre 200: lo que impide crear la operación va en violations.</summary>
    public async Task<OperationPreviewDto> PreviewAsync(Guid agentId, OperationPreviewRequest request,
        CancellationToken ct = default)
    {
        currentUser.EnsureCanOperateAgent(agentId);
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        var balance = agent.Balance
                      ?? throw new InvalidOperationException($"El corresponsal {agent.Id} no tiene agent_balances");
        var fee = await fees.GetCurrentAsync(ct);
        var reserved = await operations.GetReservationsAsync(agentId, ct);
        // [Required] en el DTO garantiza los tres valores.
        return OperationRules.Preview(
            request.Type!.Value, request.RequestedAmount!.Value, request.RoundAmount!.Value, fee, balance, reserved);
    }

    /// <summary>
    /// Crea el depósito en pending reservando el monto cobrado de cupo y pide al riel pagar el neto a la billetera
    /// del usuario. El cupo queda bloqueado (FOR UPDATE) también durante la llamada al riel: así dos depósitos
    /// simultáneos no pueden gastar la misma reserva.
    /// </summary>
    public async Task<OperationDto> CreateDepositAsync(Guid agentId, string idempotencyKey,
        CreateDepositRequest request, CancellationToken ct = default)
    {
        currentUser.EnsureCanOperateAgent(agentId);
        // [Required] en el DTO garantiza los valores; cashReceivedConfirmed solo admite true.
        var requested = request.RequestedAmount!.Value;
        var round = request.RoundAmount!.Value;
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        var key = await keys.GetByPhoneAsync(request.Phone, ct) ?? throw ApiErrors.KeyNotFound();

        return await unitOfWork.InTransactionAsync(async tx =>
        {
            var balance = await ledger.LockAsync(agentId, tx);
            var fee = await fees.GetCurrentAsync(tx);
            if (requested <= Math.Max(fee.Total, OperationRules.MinimumRequestedAmount))
                throw ApiErrors.AmountTooLow();
            var amounts = OperationRules.Calculate(requested, round, fee);
            var reserved = await operations.GetReservationsAsync(agentId, tx);
            var available = balance.Quota - reserved.QuotaReserved;
            if (amounts.ChargedAmount > available) throw ApiErrors.InsufficientQuota(amounts.ChargedAmount - available);

            var now = time.GetUtcNow();
            var operation = AddCashOperation(OperationKind.Deposit, agentId, key, requested, round, fee, amounts,
                idempotencyKey, now);
            var payment = new Payment
            {
                Id = RailPaymentId(agentId, "deposit", idempotencyKey),
                OperationId = operation.Id,
                Rail = PaymentRail.OpenPayments,
                Direction = PaymentDirection.Outgoing,
                State = OperationState.Pending,
                Amount = amounts.NetAmount,
                SourceWalletAddress = options.Value.WalletAddress,
                DestinationWalletAddress = key.WalletAddress,
                CreatedAt = now,
            };
            payments.Add(payment);
            // Se guarda antes de llamar al riel: si la base rechaza algo, no se llega a pedir el pago.
            await unitOfWork.SaveChangesAsync(tx);

            var reading = await CreateRailPaymentAsync(payment, new RailPaymentRequest(
                RailDirection.Outgoing, key.WalletAddress, RailAmount.FromCop(amounts.NetAmount),
                Description: RailDescription($"Depósito en {agent.Name}")),
                tx);
            await sync.ApplyAsync(operation, payment, reading, "create", ct: tx);
            return await ToDtoAsync(operation, payment, tx);
        }, ct);
    }

    /// <summary>
    /// Crea el retiro en pending reservando el neto del efectivo y le cobra al usuario el monto cobrado (incoming,
    /// 5 min para aprobar). El usuario recibe la approvalUrl por SMS simulado. El efectivo se entrega solo cuando
    /// la operación está completed.
    /// </summary>
    public async Task<OperationDto> CreateWithdrawalAsync(Guid agentId, string idempotencyKey,
        CreateWithdrawalRequest request, CancellationToken ct = default)
    {
        currentUser.EnsureCanOperateAgent(agentId);
        // [Required] en el DTO garantiza los valores.
        var requested = request.RequestedAmount!.Value;
        var round = request.RoundAmount!.Value;
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        var key = await keys.GetByPhoneAsync(request.Phone, ct) ?? throw ApiErrors.KeyNotFound();

        return await unitOfWork.InTransactionAsync(async tx =>
        {
            var balance = await ledger.LockAsync(agentId, tx);
            var fee = await fees.GetCurrentAsync(tx);
            if (requested <= Math.Max(fee.Total, OperationRules.MinimumRequestedAmount))
                throw ApiErrors.AmountTooLow();
            var amounts = OperationRules.Calculate(requested, round, fee);
            var reserved = await operations.GetReservationsAsync(agentId, tx);
            var available = balance.CashOnHand - reserved.CashReserved;
            if (amounts.NetAmount > available) throw ApiErrors.InsufficientCash(amounts.NetAmount - available);

            var now = time.GetUtcNow();
            var expiresAt = ApprovalDeadline(now);
            var operation = AddCashOperation(OperationKind.Withdrawal, agentId, key, requested, round, fee, amounts,
                idempotencyKey, now);
            var payment = new Payment
            {
                Id = RailPaymentId(agentId, "withdrawal", idempotencyKey),
                OperationId = operation.Id,
                Rail = PaymentRail.OpenPayments,
                Direction = PaymentDirection.Incoming,
                State = OperationState.Pending,
                Amount = amounts.ChargedAmount,
                SourceWalletAddress = key.WalletAddress,
                DestinationWalletAddress = options.Value.WalletAddress,
                ExpiresAt = expiresAt,
                CreatedAt = now,
            };
            payments.Add(payment);
            await unitOfWork.SaveChangesAsync(tx);

            var reading = await CreateRailPaymentAsync(payment, new RailPaymentRequest(
                RailDirection.Incoming, key.WalletAddress, RailAmount.FromCop(amounts.ChargedAmount), expiresAt,
                RailDescription($"Retiro de {amounts.NetAmount.ToString("C0", PesoFormat)} en {agent.Name}")),
                tx);
            await sync.ApplyAsync(operation, payment, reading, "create", ct: tx);
            if (operation.State == OperationState.Pending && payment.ApprovalUrl is { } approvalUrl)
            {
                inbox.Send(MessageChannel.Sms, MessageKind.PaymentRequest, key.Phone,
                    $"Relia: {agent.Name} te pide {amounts.ChargedAmount.ToString("C0", PesoFormat)} para entregarte " +
                    $"{amounts.NetAmount.ToString("C0", PesoFormat)} en efectivo ({operation.Reference}). " +
                    $"Aprueba en tu billetera antes de {WalletApprovalWindow.TotalMinutes:0} minutos: {approvalUrl}",
                    approvalUrl, operation.Id);
                await unitOfWork.SaveChangesAsync(tx);
            }
            return await ToDtoAsync(operation, payment, tx);
        }, ct);
    }

    /// <summary>
    /// Carga de cupo sin tarifa (neto = cobrado = monto). Bre-B: QR simulado por 10 min, sin riel; el pago lo marca
    /// recibido el Modo demo. Wallet: Relia le cobra a la billetera registrada del corresponsal (incoming, 5 min).
    /// No reserva nada: el cupo solo sube al completarse.
    /// </summary>
    public async Task<OperationDto> CreateQuotaTopUpAsync(Guid agentId, string idempotencyKey,
        CreateQuotaTopUpRequest request, CancellationToken ct = default)
    {
        currentUser.EnsureCanOperateAgent(agentId);
        // [Required] en el DTO garantiza los valores.
        var method = request.Method!.Value;
        var amount = request.RequestedAmount!.Value;
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();

        return await unitOfWork.InTransactionAsync(async tx =>
        {
            var now = time.GetUtcNow();
            var breB = method == TopUpMethod.BreB;
            var operation = new Operation
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                Kind = OperationKind.QuotaTopUp,
                TopUpMethod = breB ? TopUpMethodKind.BreB : TopUpMethodKind.Wallet,
                RequestedAmount = amount,
                NetAmount = amount,
                ChargedAmount = amount,
                State = OperationState.Pending,
                IdempotencyKey = idempotencyKey,
                CreatedAt = now,
            };
            operations.Add(operation);
            operations.AddEvent(new OperationEvent
            {
                OperationId = operation.Id, NewState = OperationState.Pending, Origin = EventOrigin.Agent, CreatedAt = now,
            });
            var payment = new Payment
            {
                Id = RailPaymentId(agentId, "quota-top-up", idempotencyKey),
                OperationId = operation.Id,
                Rail = breB ? PaymentRail.BreBSimulated : PaymentRail.OpenPayments,
                Direction = PaymentDirection.Incoming,
                State = OperationState.Pending,
                Amount = amount,
                SourceWalletAddress = agent.WalletAddress,
                DestinationWalletAddress = options.Value.WalletAddress,
                ExpiresAt = breB ? now + BreBQrWindow : ApprovalDeadline(now),
                CreatedAt = now,
            };
            payments.Add(payment);
            await unitOfWork.SaveChangesAsync(tx);

            if (breB)
            {
                // El QR lleva la referencia, que la base asigna al insertar.
                payment.RailReference = $"BREB-{operation.Reference}";
                payment.RailDetail = Mapping.SimulatedRailDetail(
                    $"breb://relia.co/pay?ref={operation.Reference}&amount={amount}&expires={payment.ExpiresAt:O}");
                await unitOfWork.SaveChangesAsync(tx);
                return await ToDtoAsync(operation, payment, tx);
            }

            var reading = await CreateRailPaymentAsync(payment, new RailPaymentRequest(
                RailDirection.Incoming, agent.WalletAddress, RailAmount.FromCop(amount), payment.ExpiresAt,
                RailDescription($"Carga de cupo de {amount.ToString("C0", PesoFormat)} para {agent.Name}")),
                tx);
            await sync.ApplyAsync(operation, payment, reading, "create", ct: tx);
            return await ToDtoAsync(operation, payment, tx);
        }, ct);
    }

    /// <summary>Completa la carga Bre-B pendiente como si hubiera llegado el pago: el cupo sube el monto.</summary>
    public async Task<OperationDto> SimulateBreBPaymentAsync(Guid operationId, CancellationToken ct = default)
    {
        var completed = await unitOfWork.InTransactionAsync(async tx =>
        {
            var operation = await operations.GetForUpdateAsync(operationId, tx) ?? throw ApiErrors.NotFound();
            if (operation.Kind != OperationKind.QuotaTopUp || operation.TopUpMethod != TopUpMethodKind.BreB
                || operation.State != OperationState.Pending)
                throw ApiErrors.OperationNotCancellable();
            var payment = await payments.GetLatestByOperationForUpdateAsync(operationId, tx)
                          ?? throw new InvalidOperationException($"La carga {operation.Reference} no tiene pago");
            var now = time.GetUtcNow();
            // QR vencido: ya no es una carga pendiente aunque el barrido (ticket 11) aún no la haya cerrado.
            if (payment.ExpiresAt <= now) throw ApiErrors.OperationNotCancellable();
            payments.AddEvent(new PaymentEvent
            {
                PaymentId = payment.Id, Action = "simulate", PreviousState = payment.State,
                NewState = OperationState.Completed, Origin = EventOrigin.Simulator,
                Detail = "Pago Bre-B simulado desde el Modo demo", CreatedAt = now,
            });
            await sync.CompleteAsync(operation, payment, now, EventOrigin.Simulator, tx);
            return operation;
        }, ct);
        return await ToDtoAsync(completed, await payments.GetLatestByOperationAsync(operationId, ct), ct);
    }

    /// <summary>403 si es de otro corresponsal. Si está pendiente, refresca antes el pago desde el proxy.</summary>
    public async Task<OperationDto> GetAsync(Guid operationId, CancellationToken ct = default)
    {
        var operation = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanReadAgent(operation.AgentId);
        await sync.RefreshAsync(operation, ct);
        var payment = await payments.GetLatestByOperationAsync(operation.Id, ct);
        return await ToDtoAsync(operation, payment, ct);
    }

    /// <summary>
    /// Retiros y cargas pendientes, antes de que el pagador apruebe: cancela en el riel y deja la operación
    /// cancelled/request_cancelled, liberando la reserva. Si el riel ya no deja cancelar (payment_not_cancellable),
    /// o la operación ya no está pendiente, 409 operation_not_cancellable y la operación sigue su curso.
    /// </summary>
    public async Task<OperationDto> CancelAsync(Guid operationId, CancellationToken ct = default)
    {
        var operation = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanOperateAgent(operation.AgentId);
        if (operation.Kind == OperationKind.Deposit || operation.State != OperationState.Pending)
            throw ApiErrors.OperationNotCancellable();
        var payment = await payments.GetLatestByOperationAsync(operation.Id, ct)
                      ?? throw new InvalidOperationException($"La operación {operation.Reference} no tiene pago");

        // La llamada al riel va fuera de la transacción, como el refresco.
        RailPayment? reading = null;
        if (payment.RailPaymentId is { } railPaymentId)
        {
            try
            {
                reading = await rail.CancelPaymentAsync(railPaymentId, ct);
            }
            catch (RailUnavailableException e)
            {
                logger.LogWarning(e, "No se pudo cancelar el pago {PaymentId} de {Reference}", payment.Id, operation.Reference);
                throw ApiErrors.RailUnavailable();
            }
            catch (RailException e) when (e.Code == "payment_not_cancellable")
            {
                // El usuario ya aprobó: se refresca para que la operación muestre su estado real.
                await sync.RefreshAsync(operation, ct);
                throw ApiErrors.OperationNotCancellable();
            }
            catch (RailException e)
            {
                logger.LogError(e, "Respuesta inesperada del riel al cancelar el pago {PaymentId} ({Code})",
                    payment.Id, e.Code);
                throw ApiErrors.RailUnavailable();
            }
        }
        else if (payment.Rail != PaymentRail.BreBSimulated)
        {
            // Un pago de Open Payments siempre tiene id del proxy: la creación se deshace si el proxy no responde.
            throw new InvalidOperationException($"El pago {payment.Id} de {operation.Reference} no tiene id del proxy");
        }

        var cancelled = await unitOfWork.InTransactionAsync(async tx =>
        {
            var locked = await operations.GetForUpdateAsync(operation.Id, tx);
            var lockedPayment = await payments.GetLatestByOperationForUpdateAsync(operation.Id, tx);
            if (locked is null || lockedPayment is null) return false;
            if (reading is not null)
                await sync.ApplyAsync(locked, lockedPayment, reading, "cancel", EventOrigin.Agent, tx);
            else if (locked.State == OperationState.Pending)
                await sync.CloseWithoutMovementAsync(locked, lockedPayment, OperationState.Cancelled,
                    "request_cancelled", "Cancelada por el corresponsal", EventOrigin.Agent, tx);
            return locked.State == OperationState.Cancelled;
        }, ct);
        // Lo aplicado queda guardado aunque responda 409: p. ej. el riel ya la había completado.
        if (!cancelled) throw ApiErrors.OperationNotCancellable();

        var current = await operations.GetByIdAsync(operation.Id, ct) ?? throw ApiErrors.NotFound();
        return await ToDtoAsync(current, await payments.GetLatestByOperationAsync(operation.Id, ct), ct);
    }

    /// <summary>
    /// Solo retiros completed: llena cashHandedOverAt y deja un evento de auditoría, sin tocar saldos ni estado.
    /// Repetirla devuelve la misma operación sin escribir nada.
    /// </summary>
    public async Task<OperationDto> ConfirmCashHandoverAsync(Guid operationId, CancellationToken ct = default)
    {
        var operation = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanOperateAgent(operation.AgentId);
        // ck_op_cash_handover: solo retiros completed. Para el contrato, otro tipo tampoco es "un retiro completado".
        if (operation.Kind != OperationKind.Withdrawal || operation.State != OperationState.Completed)
            throw ApiErrors.OperationNotCompleted();

        if (operation.CashHandedOverAt is null)
        {
            await unitOfWork.InTransactionAsync(async tx =>
            {
                var locked = await operations.GetForUpdateAsync(operation.Id, tx)
                             ?? throw ApiErrors.NotFound();
                if (locked.CashHandedOverAt is not null) return false;
                var now = time.GetUtcNow();
                locked.CashHandedOverAt = now;
                operations.AddEvent(new OperationEvent
                {
                    OperationId = locked.Id,
                    PreviousState = OperationState.Completed,
                    NewState = OperationState.Completed,
                    Origin = EventOrigin.Agent,
                    Detail = "Efectivo entregado al usuario",
                    CreatedAt = now,
                });
                await unitOfWork.SaveChangesAsync(tx);
                return true;
            }, ct);
        }

        return await ToDtoAsync(operation, await payments.GetLatestByOperationAsync(operation.Id, ct), ct);
    }

    /// <summary>
    /// Plazo de aprobación redondeado hacia arriba al minuto: entre 5 y 6 min. El proxy incluye expiresAt en la
    /// huella de idempotencia, así que un reintento dentro del mismo minuto manda el mismo cuerpo y recupera el mismo
    /// pago; cruzando el minuto responde 409 idempotency_conflict.
    /// </summary>
    private static DateTimeOffset ApprovalDeadline(DateTimeOffset now)
    {
        var minute = TimeSpan.TicksPerMinute;
        var ceiling = (now.UtcTicks + minute - 1) / minute * minute;
        return new DateTimeOffset(ceiling, TimeSpan.Zero) + WalletApprovalWindow;
    }

    /// <summary>Depósito o retiro en pending con los montos del desglose y su evento de creación.</summary>
    private Operation AddCashOperation(OperationKind kind, Guid agentId, WalletKey key, long requested, bool round,
        Fee fee, Breakdown amounts, string idempotencyKey, DateTimeOffset now)
    {
        var operation = new Operation
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            Kind = kind,
            WalletKeyId = key.Id,
            WalletKey = key,
            RequestedAmount = requested,
            RoundAmount = round,
            FeeId = fee.Id,
            FeeTotal = amounts.Total,
            FeeRelia = amounts.ReliaShare,
            FeeAgent = amounts.AgentShare,
            NetAmount = amounts.NetAmount,
            ChargedAmount = amounts.ChargedAmount,
            State = OperationState.Pending,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
        };
        operations.Add(operation);
        operations.AddEvent(new OperationEvent
        {
            OperationId = operation.Id, NewState = OperationState.Pending, Origin = EventOrigin.Agent, CreatedAt = now,
        });
        return operation;
    }

    /// <summary>
    /// Descripción que ve el pagador, recortada al tope del proxy. Los llamadores no ponen el OP-xxxxxx: el número
    /// cambia en cada reintento y el proxy vería otro cuerpo con la misma clave.
    /// </summary>
    private static string RailDescription(string text) =>
        text.Length > MaxRailDescription ? text[..MaxRailDescription] : text;

    /// <summary>
    /// POST /payments al proxy. Riel caído o sin permiso → 503 rail_unavailable; billetera del usuario que el riel
    /// no acepta → 422 wallet_not_found. En los dos casos el filtro deshace la operación.
    /// </summary>
    private async Task<RailPayment> CreateRailPaymentAsync(Payment payment, RailPaymentRequest request,
        CancellationToken ct)
    {
        try
        {
            return await rail.CreatePaymentAsync(payment.Id, request, ct);
        }
        catch (RailUnavailableException e)
        {
            logger.LogWarning(e, "El riel no aceptó el pago {PaymentId}", payment.Id);
            throw ApiErrors.RailUnavailable();
        }
        catch (RailException e) when (e.Code is "wallet_not_found" or "unsupported_asset" or "same_wallet")
        {
            logger.LogWarning(e, "El riel rechazó la billetera {Wallet} ({Code})", request.Counterparty, e.Code);
            throw ApiErrors.WalletNotFound();
        }
        catch (RailException e) when (e.Code == "idempotency_conflict")
        {
            // Se reintentó la misma Idempotency-Key con otro cuerpo después de un 503.
            throw new ApiException(StatusCodes.Status409Conflict, "idempotency_conflict",
                "La clave de idempotencia ya se usó con otra petición");
        }
        catch (RailException e)
        {
            logger.LogError(e, "Respuesta inesperada del riel al crear el pago {PaymentId} ({Code})", payment.Id, e.Code);
            throw ApiErrors.RailUnavailable();
        }
    }

    /// <summary>
    /// payments.id, que también es la Idempotency-Key del riel, derivado de (corresponsal, tipo, clave del frontend).
    /// Si el riel creó el pago pero la respuesta no llegó (timeout → 503 y rollback), el reintento con la misma
    /// clave le pide al riel el mismo pago en vez de crear otro: no se paga dos veces.
    /// </summary>
    private static Guid RailPaymentId(Guid agentId, string kind, string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{agentId:N}:{kind}:{idempotencyKey}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private async Task<OperationDto> ToDtoAsync(Operation operation, Payment? payment, CancellationToken ct)
    {
        var own = operation.State == OperationState.Completed
            ? await ledgerEntries.GetByOperationAsync(operation.Id, ct)
            : null;
        var lastKnown = own is null
            ? await ledgerEntries.GetLatestByAgentAsync(operation.AgentId, operation.UpdatedAt, ct)
            : null;
        return operation.ToDto(payment, Mapping.ToBalanceChange(own, lastKnown));
    }
}
