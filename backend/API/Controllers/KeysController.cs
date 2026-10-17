using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Relia.Api.Contracts;
using Relia.Api.Idempotency;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

[ApiController]
[Route("keys/{phone:regex(^\\+[[1-9]][[0-9]]{{7,14}}$)}")]
[Authorize]
[Tags("keys")]
public class KeysController(IKeyService keys) : ControllerBase
{
    [HttpGet]
    public Task<KeyLookupDto> Lookup(string phone, CancellationToken ct) => keys.LookupAsync(phone, ct);

    [HttpPost("invitations")]
    [Idempotent]
    public async Task<ActionResult<KeyInvitationDto>> Invite(
        string phone,
        [FromHeader(Name = "Idempotency-Key"), Required, StringLength(128, MinimumLength = 8)] string idempotencyKey,
        CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await keys.InviteAsync(phone, idempotencyKey, ct));
}
