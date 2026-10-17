using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>POST /auth/login y GET /me.</summary>
public class AuthService(
    IAgentRepository agents, IAdminRepository admins, IOperationRepository operations, ICurrentUser currentUser,
    JwtTokenIssuer tokens) : IAuthService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        // El correo es único por tabla; se busca primero entre corresponsales y luego entre admins.
        var agent = await agents.GetByEmailAsync(request.Email, ct);
        if (agent is not null)
        {
            if (!agent.Active || !PasswordHashing.Verify(agent.Password, request.Password))
                throw InvalidCredentials();
            return new LoginResponse(
                tokens.Issue(agent.Id, Roles.Agent, agent.Id), tokens.ExpiresInSeconds, Role.Agent, agent.Id);
        }

        var admin = await admins.GetByEmailAsync(request.Email, ct);
        if (admin is null || !admin.Active || !PasswordHashing.Verify(admin.Password, request.Password))
            throw InvalidCredentials();
        return new LoginResponse(
            tokens.Issue(admin.Id, Roles.Admin, null), tokens.ExpiresInSeconds, Role.Admin, null);
    }

    public async Task<MeDto> GetMeAsync(CancellationToken ct = default)
    {
        if (currentUser.Role == Role.Admin)
        {
            var admin = await admins.GetByIdAsync(currentUser.UserId, ct);
            if (admin is null || !admin.Active) throw Unauthorized();
            return new MeDto(Role.Admin, admin.Email, admin.Name, null);
        }

        // Tras un reinicio de la demo el token puede apuntar a un corresponsal que ya no existe.
        var agent = currentUser.AgentId is { } agentId ? await agents.GetByIdAsync(agentId, ct) : null;
        if (agent is null || !agent.Active) throw Unauthorized();
        var reservations = await operations.GetReservationsAsync(agent.Id, ct);
        return new MeDto(Role.Agent, agent.Email, agent.ManagerName, agent.ToDto(reservations));
    }

    private static ApiException InvalidCredentials() =>
        new(StatusCodes.Status401Unauthorized, "invalid_credentials", "Correo o contraseña incorrectos");

    private static ApiException Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, "unauthorized", "Sin sesión");
}
