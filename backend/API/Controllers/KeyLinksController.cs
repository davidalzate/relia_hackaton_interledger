using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Relia.Api.Contracts;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

/// <summary>Página pública "Vincular billetera": sin login.</summary>
[ApiController]
[Route("key-links")]
[AllowAnonymous]
[Tags("keyLinks")]
public class KeyLinksController(IKeyLinkService keyLinks) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<KeyLinkChallengeDto>> Start(CreateKeyLinkRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await keyLinks.StartAsync(request, ct));

    [HttpPost("{challengeId:guid}/verify")]
    public Task<KeyLinkDto> Verify(Guid challengeId, VerifyKeyLinkRequest request, CancellationToken ct) =>
        keyLinks.VerifyAsync(challengeId, request, ct);
}
