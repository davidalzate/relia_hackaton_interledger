using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Relia.Api.Configuration;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

/// <summary>Modo demo: sin login, y solo con DEMO_MODE=true.</summary>
[ApiController]
[Route("demo")]
[AllowAnonymous]
[DemoOnly]
[Tags("demo")]
public class DemoController(ISimulatedInbox inbox) : ControllerBase
{
    [HttpGet("messages")]
    public Task<DemoMessagePageDto> ListMessages(
        [FromQuery, RegularExpression(Patterns.Phone)] string? phone,
        [FromQuery] string? cursor,
        [FromQuery, Range(1, 100)] int limit = 20,
        CancellationToken ct = default) =>
        inbox.ListAsync(phone, cursor, limit, ct);

    [HttpPost("messages/read")]
    public async Task<IActionResult> MarkMessagesRead(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MarkDemoMessagesReadRequest? request,
        CancellationToken ct)
    {
        await inbox.MarkReadAsync(request?.Phone, ct);
        return NoContent();
    }
}

/// <summary>
/// Sin DEMO_MODE=true la ruta no existe: 404 not_found antes de validar el modelo o ejecutar la acción.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class DemoOnlyAttribute() : TypeFilterAttribute(typeof(DemoOnlyFilter));

public class DemoOnlyFilter(IOptions<ReliaOptions> options) : IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (!options.Value.DemoMode) throw ApiErrors.NotFound();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
