using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Idempotency;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

/// <summary>Rutas bajo /agents/{agentId} que usa el corresponsal.</summary>
[ApiController]
[Route("agents/{agentId:guid}")]
[Authorize]
public class AgentsController(
    IAgentService agents, ICashAdjustmentService cashAdjustments, IOperationService operations) : ControllerBase
{
    [HttpGet]
    [Tags("agents")]
    public Task<AgentDto> GetAgent(Guid agentId, CancellationToken ct) => agents.GetAgentAsync(agentId, ct);

    [HttpGet("history")]
    [Tags("agents")]
    public Task<HistoryPageDto> GetHistory(
        Guid agentId,
        [FromQuery] HistoryEntryKind? kind,
        [FromQuery] OperationStatus? status,
        [FromQuery] string? cursor,
        [FromQuery, Range(1, 100)] int limit = 20,
        CancellationToken ct = default) =>
        agents.GetHistoryAsync(agentId, kind, status, cursor, limit, ct);

    [HttpPost("cash-adjustments")]
    [Tags("cash")]
    [Authorize(Roles = Roles.Agent)]
    [Idempotent]
    public async Task<ActionResult<CashAdjustmentDto>> CreateCashAdjustment(
        Guid agentId,
        [FromHeader(Name = "Idempotency-Key"), Required, StringLength(128, MinimumLength = 8)] string idempotencyKey,
        CreateCashAdjustmentRequest request,
        CancellationToken ct)
    {
        var adjustment = await cashAdjustments.CreateAsync(agentId, idempotencyKey, request, ct);
        return StatusCode(StatusCodes.Status201Created, adjustment);
    }

    [HttpPost("operations/preview")]
    [Tags("operations")]
    [Authorize(Roles = Roles.Agent)]
    public Task<OperationPreviewDto> Preview(Guid agentId, OperationPreviewRequest request, CancellationToken ct) =>
        operations.PreviewAsync(agentId, request, ct);

    [HttpPost("deposits")]
    [Tags("operations")]
    [Authorize(Roles = Roles.Agent)]
    [Idempotent]
    public async Task<ActionResult<OperationDto>> CreateDeposit(
        Guid agentId,
        [FromHeader(Name = "Idempotency-Key"), Required, StringLength(128, MinimumLength = 8)] string idempotencyKey,
        CreateDepositRequest request,
        CancellationToken ct) =>
        Accepted(await operations.CreateDepositAsync(agentId, idempotencyKey, request, ct));

    [HttpPost("withdrawals")]
    [Tags("operations")]
    [Authorize(Roles = Roles.Agent)]
    [Idempotent]
    public async Task<ActionResult<OperationDto>> CreateWithdrawal(
        Guid agentId,
        [FromHeader(Name = "Idempotency-Key"), Required, StringLength(128, MinimumLength = 8)] string idempotencyKey,
        CreateWithdrawalRequest request,
        CancellationToken ct) =>
        Accepted(await operations.CreateWithdrawalAsync(agentId, idempotencyKey, request, ct));

    [HttpPost("quota-top-ups")]
    [Tags("operations")]
    [Authorize(Roles = Roles.Agent)]
    [Idempotent]
    public async Task<ActionResult<OperationDto>> CreateQuotaTopUp(
        Guid agentId,
        [FromHeader(Name = "Idempotency-Key"), Required, StringLength(128, MinimumLength = 8)] string idempotencyKey,
        CreateQuotaTopUpRequest request,
        CancellationToken ct) =>
        Accepted(await operations.CreateQuotaTopUpAsync(agentId, idempotencyKey, request, ct));

    // 202 con Location: /operations/{operationId}.
    private AcceptedResult Accepted(OperationDto operation) =>
        base.Accepted($"/operations/{operation.Id}", operation);
}
