using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Relia.Api.Errors;

namespace Relia.Api.Services.Support;

/// <summary>
/// Idempotencia de las creaciones por Idempotency-Key. Misma clave y mismo cuerpo → la misma respuesta; misma
/// clave y otro cuerpo → 409 idempotency_conflict. Un error (excepción) no se guarda: se puede reintentar.
/// En memoria del proceso: el esquema solo tiene columna de idempotencia en operations, que además se
/// verifica en la base (ver OperationService).
/// </summary>
public class IdempotencyStore
{
    private record Entry(string RequestHash, string ResponseJson);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <param name="scope">Quién y qué ruta: la clave solo choca dentro del mismo scope.</param>
    public async Task<T> RunAsync<T>(string scope, string key, object request, Func<Task<T>> create)
    {
        var id = $"{scope}|{key}";
        var hash = Hash(request);
        var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_entries.TryGetValue(id, out var existing))
            {
                if (existing.RequestHash != hash) throw ApiErrors.IdempotencyConflict();
                return JsonSerializer.Deserialize<T>(existing.ResponseJson)!;
            }

            var response = await create();
            _entries[id] = new Entry(hash, JsonSerializer.Serialize(response));
            return response;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Hash del cuerpo, para comparar repeticiones.</summary>
    public static string Hash(object request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));

    public void Clear() => _entries.Clear();
}
