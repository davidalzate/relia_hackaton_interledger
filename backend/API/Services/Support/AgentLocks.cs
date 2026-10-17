using System.Collections.Concurrent;

namespace Relia.Api.Services.Support;

/// <summary>
/// Candado en memoria por corresponsal. Serializa sus creaciones mientras se espera al riel, sin tener
/// abierta una transacción ni bloqueada la fila de saldos durante la llamada HTTP. Vale para una sola
/// instancia del backend; la transacción final vuelve a verificar los saldos bajo FOR UPDATE.
/// </summary>
public class AgentLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid agentId, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(agentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
