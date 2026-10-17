using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

[ApiController]
[Authorize(Roles = Roles.Admin)]
[Tags("admin")]
public class AdminController(IAdminService admin, ISettlementService settlements) : ControllerBase
{
    [HttpGet("agents")]
    public Task<AgentListDto> ListAgents(CancellationToken ct) => admin.ListAgentsAsync(ct);

    [HttpPost("agents/{agentId:guid}/settlements")]
    [Tags("settlements")]
    public async Task<ActionResult<SettlementDto>> CreateSettlement(
        Guid agentId,
        [FromHeader(Name = "Idempotency-Key"), Required, StringLength(128, MinimumLength = 8)] string idempotencyKey,
        CancellationToken ct)
    {
        var settlement = await settlements.CreateAsync(agentId, idempotencyKey, ct);
        return Accepted($"/settlements/{settlement.Id}", settlement);
    }
}
