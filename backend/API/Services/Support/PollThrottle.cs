using System.Collections.Concurrent;

namespace Relia.Api.Services.Support;

/// <summary>Limita la consulta al riel a una vez por segundo por pago (el frontend consulta cada 1 a 2 s).</summary>
public class PollThrottle(TimeProvider time)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _last = new();

    /// <summary>true si pasó al menos un segundo desde la última consulta de ese pago.</summary>
    public bool TryAcquire(Guid paymentId)
    {
        var now = time.GetUtcNow();
        var acquired = false;
        _last.AddOrUpdate(paymentId,
            _ => { acquired = true; return now; },
            (_, last) =>
            {
                if (now - last < Interval) return last;
                acquired = true;
                return now;
            });
        return acquired;
    }
}
