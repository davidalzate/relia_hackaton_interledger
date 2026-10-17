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
/// Desglose, depósitos y lectura de operaciones. Retiro (ticket 09) y carga de cupo (ticket 10) siguen en
/// desarrollo. La idempotencia de las creaciones la resuelve el filtro [Idempotent] del controlador, que abre la
/// transacción: un 4xx o un 503 deshacen todo, incluido lo que se escribió antes de llamar al riel.
/// </summary>
public class OperationService(
    IAgentRepository agents, IFeeRepository fees, IWalletKeyRepository keys, IOperationRepository operations,
    IPaymentRepository payments, ILedgerRepository ledgerEntries, ILedger ledger, IRailClient rail,
    IOperationSync sync, IUnitOfWork unitOfWork, ICurrentUser currentUser, IOptions<ReliaOptions> options,
    TimeProvider time, ILogger<OperationService> logger) : IOperationService
{
    /// <summary>Tope de la descripción que ve el pagador en su billetera (rail-proxy.openapi.yaml).</summary>
    private const int MaxRailDescription = 140;

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
            var operation = new Operation
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                Kind = OperationKind.Deposit,
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

            // Sin el OP-xxxxxx: el número cambia en cada reintento y el proxy vería otro cuerpo con la misma clave.
            var description = $"Depósito en {agent.Name}";
            var reading = await CreateRailPaymentAsync(payment, new RailPaymentRequest(
                RailDirection.Outgoing, key.WalletAddress, RailAmount.FromCop(amounts.NetAmount),
                Description: description.Length > MaxRailDescription ? description[..MaxRailDescription] : description),
                tx);
            await sync.ApplyAsync(operation, payment, reading, "create", tx);
            return await ToDtoAsync(operation, payment, tx);
        }, ct);
    }

    public Task<OperationDto> CreateWithdrawalAsync(Guid agentId, string idempotencyKey,
        CreateWithdrawalRequest request, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("createWithdrawal");

    public Task<OperationDto> CreateQuotaTopUpAsync(Guid agentId, string idempotencyKey,
        CreateQuotaTopUpRequest request, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("createQuotaTopUp");

    /// <summary>403 si es de otro corresponsal. Si está pendiente, refresca antes el pago desde el proxy.</summary>
    public async Task<OperationDto> GetAsync(Guid operationId, CancellationToken ct = default)
    {
        var operation = await operations.GetByIdAsync(operationId, ct) ?? throw ApiErrors.NotFound();
        currentUser.EnsureCanReadAgent(operation.AgentId);
        await sync.RefreshAsync(operation, ct);
        var payment = await payments.GetLatestByOperationAsync(operation.Id, ct);
        return await ToDtoAsync(operation, payment, ct);
    }

    public Task<OperationDto> CancelAsync(Guid operationId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("cancelOperation");

    public Task<OperationDto> ConfirmCashHandoverAsync(Guid operationId, CancellationToken ct = default) =>
        throw new FeatureInDevelopmentException("confirmCashHandover");

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
