using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;

namespace Relia.Api.Services;

/// <summary>Ticket 02. Las claves se comparan en texto plano: así las guarda la base (decisión del hackathon).</summary>
public class AuthService(
    IAgentRepository agents, IAdminRepository admins, IAgentService agentService, ICurrentUser currentUser,
    IOptions<JwtOptions> jwt, TimeProvider time) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var agent = await agents.GetByEmailAsync(request.Email, ct);
        if (agent is { Active: true } && agent.Password == request.Password)
            return Issue(agent.Id, Role.Agent, agent.Id);

        var admin = await admins.GetByEmailAsync(request.Email, ct);
        if (admin is { Active: true } && admin.Password == request.Password)
            return Issue(admin.Id, Role.Admin, null);

        throw new ApiException(StatusCodes.Status401Unauthorized, "invalid_credentials", "Correo o contraseña incorrectos");
    }

    public async Task<MeDto> GetMeAsync(CancellationToken ct = default)
    {
        if (currentUser.Role == Role.Agent)
        {
            var agent = await agents.GetByIdAsync(currentUser.UserId, ct)
                        ?? throw ApiErrors.Unauthorized();
            return new MeDto(Role.Agent, agent.Email, agent.ContactPerson,
                await agentService.GetAgentAsync(agent.Id, ct));
        }

        var admin = await admins.GetByIdAsync(currentUser.UserId, ct) ?? throw ApiErrors.Unauthorized();
        return new MeDto(Role.Admin, admin.Email, admin.Name, null);
    }

    private LoginResponse Issue(Guid userId, Role role, Guid? agentId)
    {
        var options = jwt.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var claims = new Dictionary<string, object>
        {
            [ReliaClaims.UserId] = userId.ToString(),
            [ReliaClaims.Role] = role == Role.Agent ? Roles.Agent : Roles.Admin,
        };
        if (agentId is not null) claims[ReliaClaims.AgentId] = agentId.Value.ToString();

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(options.ExpiresInSeconds),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
        return new LoginResponse(token, options.ExpiresInSeconds, role, agentId);
    }
}
