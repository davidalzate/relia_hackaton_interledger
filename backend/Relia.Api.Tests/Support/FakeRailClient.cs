using System.Collections.Concurrent;
using Relia.Api.Rail;

namespace Relia.Api.Tests.Support;

/// <summary>
/// Doble del Rail Proxy para las pruebas HTTP. Guarda los pagos en memoria y deja que la prueba decida
/// qué pasa en el riel (aprobar, rechazar, fallar, caerse).
/// </summary>
public class FakeRailClient : IRailClient
{
    public const string ReliaWallet = "$ilp.interledger-test.dev/relia-test1";

    private readonly ConcurrentDictionary<Guid, RailPayment> _payments = new();

    /// <summary>Payment pointers que existen en el riel.</summary>
    public HashSet<string> Wallets { get; } =
    [
        "$ilp.interledger-test.dev/jeffdev",
        "$ilp.interledger-test.dev/nuevo-usuario",
        "$ilp.interledger-test.dev/rosa-test",
        ReliaWallet,
    ];

    /// <summary>true: toda llamada lanza RailUnavailableException.</summary>
    public bool Unavailable { get; set; }

    public List<CreateRailPayment> Created { get; } = [];
    public List<Guid> Cancelled { get; } = [];
    public int GetCalls { get; private set; }

    public IReadOnlyCollection<RailPayment> Payments => _payments.Values.ToList();

    public Task<RailWalletAddress?> ResolveWalletAddressAsync(string pointer, CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        return Task.FromResult(Wallets.Contains(pointer)
            ? new RailWalletAddress(pointer, "https://" + pointer[1..], "COP", 2)
            : null);
    }

    public Task<RailPayment> CreatePaymentAsync(CreateRailPayment request, CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        if (_payments.Values.FirstOrDefault(p => p.IdempotencyKey == request.IdempotencyKey) is { } existing)
            return Task.FromResult(existing);

        Created.Add(request);
        var id = Guid.NewGuid();
        var incoming = request.Direction == RailDirection.Incoming;
        var payment = new RailPayment(
            id, request.IdempotencyKey, request.Direction,
            incoming ? RailPaymentStatus.PendingApproval : RailPaymentStatus.Processing,
            incoming ? request.Counterparty : ReliaWallet,
            incoming ? ReliaWallet : request.Counterparty,
            request.ReceiveAmount, null, null,
            incoming ? $"https://auth.interledger-test.dev/interact/{id}" : null,
            request.ExpiresAt, null, null, null, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 1);
        _payments[id] = payment;
        return Task.FromResult(payment);
    }

    public Task<RailPayment?> GetPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        GetCalls++;
        return Task.FromResult(_payments.GetValueOrDefault(paymentId));
    }

    public Task<RailPayment> CancelPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        ThrowIfUnavailable();
        var payment = _payments[paymentId];
        if (payment.Status is not (RailPaymentStatus.PendingApproval or RailPaymentStatus.Cancelled))
            throw new RailPaymentNotCancellableException(paymentId);
        Cancelled.Add(paymentId);
        return Task.FromResult(Set(paymentId, RailPaymentStatus.Cancelled));
    }

    // ---- Lo que pasa en el riel, controlado por la prueba ----

    /// <summary>El pagador aprobó: el cobro pasa a processing y ya no se puede cancelar.</summary>
    public void Approve(Guid paymentId) => Set(paymentId, RailPaymentStatus.Processing);

    public void Complete(Guid paymentId) => Set(paymentId, RailPaymentStatus.Completed);

    public void Reject(Guid paymentId) => Set(paymentId, RailPaymentStatus.Rejected);

    public void Fail(Guid paymentId) => Set(paymentId, RailPaymentStatus.Failed, "insufficient_funds");

    public void Expire(Guid paymentId) => Set(paymentId, RailPaymentStatus.Expired);

    /// <summary>Simula una lectura vieja: el proxy reporta un estado con una versión dada.</summary>
    public void Overwrite(Guid paymentId, RailPaymentStatus status, int version) =>
        _payments[paymentId] = _payments[paymentId] with { Status = status, Version = version };

    /// <summary>El pago de riel creado para esa clave de idempotencia (el id del pago en el backend).</summary>
    public RailPayment ForBackendPayment(int index = -1) =>
        _payments.Values.Single(p => p.IdempotencyKey == Created[index < 0 ? Created.Count + index : index].IdempotencyKey);

    private RailPayment Set(Guid id, RailPaymentStatus status, string? failureCode = null) =>
        _payments[id] = _payments[id] with
        {
            Status = status,
            FailureCode = failureCode,
            ApprovalUrl = status == RailPaymentStatus.PendingApproval ? _payments[id].ApprovalUrl : null,
            CompletedAt = status == RailPaymentStatus.Completed ? DateTimeOffset.UtcNow : null,
            Version = _payments[id].Version + 1,
        };

    private void ThrowIfUnavailable()
    {
        if (Unavailable) throw new RailUnavailableException("riel caído en la prueba");
    }
}
