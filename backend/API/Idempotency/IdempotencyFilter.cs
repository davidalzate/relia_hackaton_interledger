using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Relia.Api.Auth;
using Relia.Api.Domain;
using Relia.Api.Errors;
using Relia.Api.Persistence;

namespace Relia.Api.Idempotency;

/// <summary>
/// Hace idempotente una creación con header <c>Idempotency-Key</c>: misma clave y mismo cuerpo devuelven la
/// respuesta guardada sin volver a ejecutar; misma clave con otro cuerpo responde 409 idempotency_conflict.
/// Solo se guardan respuestas 2xx: tras un 4xx o un 503 no queda nada y se puede reintentar con la misma clave.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class IdempotentAttribute() : TypeFilterAttribute(typeof(IdempotencyFilter));

/// <remarks>
/// <para>
/// La acción corre dentro de una transacción que abre este filtro (IUnitOfWork se une a ella), y el registro
/// se escribe en esa misma transacción: o quedan la creación y su registro, o ninguno. Un 4xx, un 503 o una
/// excepción hacen rollback de todo.
/// </para>
/// <para>
/// Alcance = método + plantilla de la ruta + actor, para que dos corresponsales puedan usar la misma clave.
/// La huella es el SHA-256 de los argumentos ya validados (cuerpo y valores de ruta), así que no depende de
/// cómo se escribió la URL. Un advisory lock de transacción sobre (alcance, clave) serializa peticiones
/// simultáneas: la segunda espera y encuentra el registro de la primera.
/// </para>
/// </remarks>
public class IdempotencyFilter(ReliaDbContext db, ICurrentUser currentUser, IOptions<JsonOptions> json)
    : IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(key))
        {
            // El header es obligatorio en el contrato; si falta, la validación del modelo ya respondió 422.
            await next();
            return;
        }

        var route = context.ActionDescriptor.AttributeRouteInfo?.Template ?? http.Request.Path.ToString();
        var scope = $"{http.Request.Method} {route}|{currentUser.Role}:{currentUser.UserId}";
        var requestHash = Hash(context.ActionArguments);

        await using var tx = await db.Database.BeginTransactionAsync(http.RequestAborted);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({scope + "\n" + key}, 0))", http.RequestAborted);

        var saved = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == scope && r.IdempotencyKey == key, http.RequestAborted);
        if (saved is not null)
        {
            if (saved.RequestHash != requestHash)
                throw new ApiException(StatusCodes.Status409Conflict, "idempotency_conflict",
                    "La clave de idempotencia ya se usó con otra petición");
            if (saved.ResourceId is not null) http.Response.Headers.Location = saved.ResourceId;
            context.Result = new ContentResult
            {
                StatusCode = saved.ResponseStatus,
                ContentType = "application/json; charset=utf-8",
                Content = saved.ResponseBody.RootElement.GetRawText(),
            };
            return;
        }

        var executed = await next();
        if (executed.Exception is not null && !executed.ExceptionHandled) return;
        if (executed.Result is not ObjectResult { Value: not null } result
            || (result.StatusCode ?? StatusCodes.Status200OK) is < 200 or >= 300)
            return;

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Scope = scope,
            IdempotencyKey = key,
            RequestHash = requestHash,
            ResponseStatus = (short)(result.StatusCode ?? StatusCodes.Status200OK),
            ResponseBody = JsonSerializer.SerializeToDocument(
                result.Value, result.Value.GetType(), json.Value.JsonSerializerOptions),
            // 202 de operaciones: Location apunta a /operations/{id}.
            ResourceId = (result as AcceptedResult)?.Location ?? (result as CreatedResult)?.Location,
        });
        // Ya se hizo el trabajo: aunque el cliente se haya ido, se confirma para que su reintento lo encuentre.
        await db.SaveChangesAsync(CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
    }

    /// <summary>SHA-256 en hex de los argumentos, sin el token de cancelación ni la propia clave.</summary>
    private string Hash(IDictionary<string, object?> arguments)
    {
        var relevant = arguments
            .Where(a => a.Key != "idempotencyKey" && a.Value is not CancellationToken)
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);
        var canonical = JsonSerializer.Serialize(relevant, json.Value.JsonSerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
