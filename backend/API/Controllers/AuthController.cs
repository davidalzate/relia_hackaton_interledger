using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Relia.Api.Contracts;
using Relia.Api.Services;

namespace Relia.Api.Controllers;

[ApiController]
[Tags("auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("auth/login")]
    [AllowAnonymous]
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, ct);

    [HttpGet("me")]
    [Authorize]
    public Task<MeDto> Me(CancellationToken ct) => auth.GetMeAsync(ct);
}
