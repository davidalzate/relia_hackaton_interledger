using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Idempotency;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

/// <summary>Liquidaciones: el admin las crea (POST /agents/{agentId}/settlements); ambos roles las leen.</summary>
[ApiController]
[Route("settlements")]
[Authorize]
[Tags("settlements")]
public class SettlementsController(ISettlementService settlements) : ControllerBase
{
    // "~/" no combina con la ruta del controlador. 202 con Location: /settlements/{settlementId}.
    [HttpPost("~/agents/{agentId:guid}/settlements")]
    [Authorize(Roles = Roles.Admin)]
    [Idempotent]
    public async Task<ActionResult<SettlementDto>> Create(
        Guid agentId,
        [FromHeader(Name = "Idempotency-Key"), Required, StringLength(128, MinimumLength = 8)] string idempotencyKey,
        CancellationToken ct)
    {
        var settlement = await settlements.CreateAsync(agentId, idempotencyKey, ct);
        return Accepted($"/settlements/{settlement.Id}", settlement);
    }

    [HttpGet]
    public Task<SettlementPageDto> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? agentId,
        [FromQuery] SettlementStatus? status,
        [FromQuery] string? cursor,
        [FromQuery, Range(1, 100)] int limit = 20,
        CancellationToken ct = default) =>
        settlements.ListAsync(new SettlementQuery(from, to, agentId, status, cursor, limit), ct);

    [HttpGet("{settlementId:guid}")]
    public Task<SettlementDto> Get(Guid settlementId, CancellationToken ct) =>
        settlements.GetAsync(settlementId, ct);
}
