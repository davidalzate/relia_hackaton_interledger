using System.Security.Claims;
using Relia.Api.Contracts;

namespace Relia.Api.Auth;

/// <summary>Claims del JWT que emite POST /auth/login.</summary>
public static class ReliaClaims
{
    public const string Role = "role";
    public const string AgentId = "agentId";
    public const string UserId = "sub";
}

public static class Roles
{
    public const string Agent = "agent";
    public const string Admin = "admin";
}

/// <summary>Quién hace la petición. Los servicios lo usan para las reglas de rol y de propiedad.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    Role Role { get; }

    /// <summary>Solo con rol agent.</summary>
    Guid? AgentId { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public Guid UserId => Guid.Parse(User.FindFirstValue(ReliaClaims.UserId)!);

    public Role Role => User.IsInRole(Roles.Admin) ? Role.Admin : Role.Agent;

    public Guid? AgentId =>
        Guid.TryParse(User.FindFirstValue(ReliaClaims.AgentId), out var id) ? id : null;
}
