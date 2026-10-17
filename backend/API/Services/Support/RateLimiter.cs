using System.Collections.Concurrent;
using Relia.Api.Errors;

namespace Relia.Api.Services.Support;

/// <summary>Ventana deslizante en memoria del proceso: como máximo <c>limit</c> intentos por clave en <c>window</c>.</summary>
public class RateLimiter(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new();

    /// <exception cref="RateLimitedException">Se superó el límite; trae los segundos que hay que esperar.</exception>
    public void Hit(string key, int limit, TimeSpan window)
    {
        var now = time.GetUtcNow();
        var hits = _hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (hits)
        {
            while (hits.Count > 0 && hits.Peek() <= now - window) hits.Dequeue();
            if (hits.Count >= limit)
            {
                var wait = hits.Peek() + window - now;
                throw new RateLimitedException(Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)));
            }
            hits.Enqueue(now);
        }
    }

    public void Clear() => _hits.Clear();
}
