using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Services.Demo;

namespace Relia.Api.Controllers;

/// <summary>Modo demo: sin login y solo con DEMO_MODE=true; sin el flag, 404.</summary>
[ApiController]
[Route("demo")]
[AllowAnonymous]
[Tags("demo")]
[DemoModeOnly]
public class DemoController(IDemoService demo) : ControllerBase
{
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        await demo.ResetAsync(ct);
        return NoContent();
    }

    [HttpGet("messages")]
    public Task<DemoMessagePageDto> Messages(
        [FromQuery, RegularExpression(Patterns.Phone)] string? phone,
        [FromQuery] string? cursor,
        [FromQuery, Range(1, 100)] int limit = 20,
        CancellationToken ct = default) =>
        demo.ListMessagesAsync(phone, cursor, limit, ct);

    [HttpGet("faults")]
    public DemoFaultsDto GetFaults() => demo.GetFaults();

    [HttpPut("faults")]
    public DemoFaultsDto SetFaults(DemoFaultsDto faults) => demo.SetFaults(faults);

    [HttpPost("operations/{operationId:guid}/expire")]
    public Task<OperationDto> Expire(Guid operationId, CancellationToken ct) => demo.ExpireNowAsync(operationId, ct);

    [HttpPost("quota-top-ups/{operationId:guid}/breb-payment")]
    public Task<OperationDto> SimulateBreBPayment(Guid operationId, CancellationToken ct) =>
        demo.SimulateBreBPaymentAsync(operationId, ct);

    [HttpGet("payment-requests")]
    public Task<DemoPaymentRequestListDto> PaymentRequests(
        [FromQuery, Required, RegularExpression(Patterns.Phone)] string phone, CancellationToken ct) =>
        demo.ListPaymentRequestsAsync(phone, ct);

    [HttpPost("messages/read")]
    public async Task<IActionResult> MarkRead(MarkMessagesReadRequest? request, CancellationToken ct)
    {
        await demo.MarkReadAsync(request?.Phone, ct);
        return NoContent();
    }
}

/// <summary>Responde 404 not_found si DEMO_MODE no es true. Filtro de recurso: corre antes de validar el modelo.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class DemoModeOnlyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue<bool>("DEMO_MODE")) throw ApiErrors.NotFound();
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
