using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

[ApiController]
[Route("operations/{operationId:guid}")]
[Authorize]
public class OperationsController(IOperationService operations, IReceiptService receipts) : ControllerBase
{
    [HttpGet]
    [Tags("operations")]
    public Task<OperationDto> Get(Guid operationId, CancellationToken ct) => operations.GetAsync(operationId, ct);

    [HttpPost("cancel")]
    [Tags("operations")]
    [Authorize(Roles = Roles.Agent)]
    public Task<OperationDto> Cancel(Guid operationId, CancellationToken ct) =>
        operations.CancelAsync(operationId, ct);

    [HttpPost("cash-handover")]
    [Tags("operations")]
    [Authorize(Roles = Roles.Agent)]
    public Task<OperationDto> ConfirmCashHandover(Guid operationId, CancellationToken ct) =>
        operations.ConfirmCashHandoverAsync(operationId, ct);

    [HttpGet("receipt")]
    [Tags("receipts")]
    public Task<ReceiptDto> GetReceipt(
        Guid operationId, [FromQuery] ReceiptViewChannel channel = ReceiptViewChannel.Screen,
        CancellationToken ct = default) =>
        receipts.GetAsync(operationId, channel, ct);

    [HttpPost("receipt/send")]
    [Tags("receipts")]
    public async Task<ActionResult<ReceiptDeliveryDto>> SendReceipt(
        Guid operationId, SendReceiptRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status202Accepted, await receipts.SendAsync(operationId, request, ct));
}
