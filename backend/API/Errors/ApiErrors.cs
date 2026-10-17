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

/// <summary>Errores comunes del contrato, con el mismo title en todas las rutas.</summary>
public static class ApiErrors
{
    public static ApiException Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, "unauthorized", "Sin sesión");

    public static ApiException Forbidden() =>
        new(StatusCodes.Status403Forbidden, "forbidden", "Sin permiso");

    public static ApiException NotFound() =>
        new(StatusCodes.Status404NotFound, "not_found", "No existe");

    /// <summary>422 insufficient_cash con lo que falta (ajustes por debajo de lo reservado, retiros).</summary>
    public static ApiException InsufficientCash(long shortfall) =>
        new(StatusCodes.Status422UnprocessableEntity, "insufficient_cash", "Efectivo insuficiente",
            new Dictionary<string, object?> { ["shortfall"] = shortfall });

    /// <summary>422 validation_error con detalle de un campo, como el de la validación de modelo.</summary>
    public static ApiException Validation(string field, string message) =>
        new(StatusCodes.Status422UnprocessableEntity, "validation_error", "Datos inválidos",
            new Dictionary<string, object?> { ["errors"] = new[] { new { field, message } } });
}

/// <summary>
/// Lanzada por los servicios aún sin implementar. Responde 501 con <c>code: not_implemented</c>, que no está en
/// el contrato: desaparece cuando todos los servicios estén implementados.
/// </summary>
public class FeatureInDevelopmentException(string feature)
    : ApiException(StatusCodes.Status501NotImplemented, "not_implemented", "En desarrollo",
        new Dictionary<string, object?> { ["feature"] = feature });

/// <summary>Convierte ApiException en application/problem+json (RFC 9457).</summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not ApiException api) return false;

        http.Response.StatusCode = api.Status;
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
