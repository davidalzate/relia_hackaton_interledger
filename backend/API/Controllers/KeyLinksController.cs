using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Relia.Api.Contracts;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

/// <summary>Página pública de vinculación: sin login y con rate limit (429 rate_limited con Retry-After).</summary>
[ApiController]
[Route("key-links")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicy)]
[Tags("keyLinks")]
public class KeyLinksController(IKeyLinkService keyLinks) : ControllerBase
{
    /// <summary>Política de Program.cs: ventana fija por IP.</summary>
    public const string RateLimitPolicy = "key-links";

    [HttpPost]
    public async Task<ActionResult<KeyLinkChallengeDto>> Start(CreateKeyLinkRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await keyLinks.StartAsync(request, ct));

    [HttpPost("{challengeId:guid}/verify")]
    public Task<KeyLinkDto> Verify(Guid challengeId, VerifyKeyLinkRequest request, CancellationToken ct) =>
        keyLinks.VerifyAsync(challengeId, request, ct);
}
