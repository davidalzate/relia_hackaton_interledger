using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Relia.Api.Contracts;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

/// <summary>Lectura de liquidaciones. Crear una (POST /agents/{agentId}/settlements) es del admin.</summary>
[ApiController]
[Route("settlements")]
[Authorize]
[Tags("settlements")]
public class SettlementsController(ISettlementService settlements) : ControllerBase
{
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
