using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Relia.Api.Errors;

/// <summary>
/// Error de negocio con un <c>code</c> del enum ErrorCode del contrato. Se responde como problem+json.
/// <paramref name="Extensions"/> lleva campos extra del Problem (shortfall, attemptsRemaining).
/// </summary>
public class ApiException(int status, string code, string title, IDictionary<string, object?>? extensions = null)
    : Exception(title)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}

/// <summary>429 rate_limited con el header Retry-After.</summary>
public class RateLimitedException(int retryAfterSeconds)
    : ApiException(StatusCodes.Status429TooManyRequests, "rate_limited", "Demasiados intentos")
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>Errores frecuentes del contrato.</summary>
public static class ApiErrors
{
    public static ApiException Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, "unauthorized", "Sin sesión");

    public static ApiException Forbidden() =>
        new(StatusCodes.Status403Forbidden, "forbidden", "Sin permiso");

    public static ApiException NotFound(string code = "not_found", string title = "No existe") =>
        new(StatusCodes.Status404NotFound, code, title);

    public static ApiException Conflict(string code, string title) =>
        new(StatusCodes.Status409Conflict, code, title);

    public static ApiException Unprocessable(string code, string title, IDictionary<string, object?>? extensions = null) =>
        new(StatusCodes.Status422UnprocessableEntity, code, title, extensions);

    public static ApiException Validation(string field, string message) =>
        Unprocessable("validation_error", "Datos inválidos", new Dictionary<string, object?>
        {
            ["errors"] = new[] { new { field, message } },
        });

    public static ApiException Shortfall(string code, string title, long shortfall) =>
        Unprocessable(code, title, new Dictionary<string, object?> { ["shortfall"] = shortfall });

    public static ApiException IdempotencyConflict() =>
        Conflict("idempotency_conflict", "La clave de idempotencia ya se usó con otro cuerpo");
}

/// <summary>Convierte ApiException en application/problem+json (RFC 9457).</summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not ApiException api) return false;

        http.Response.StatusCode = api.Status;
        if (api is RateLimitedException limited)
            http.Response.Headers.RetryAfter = limited.RetryAfterSeconds.ToString();
        var problem = new ProblemDetails { Status = api.Status, Title = api.Message, Type = "about:blank" };
        problem.Extensions["code"] = api.Code;
        foreach (var (key, value) in api.Extensions) problem.Extensions[key] = value;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
