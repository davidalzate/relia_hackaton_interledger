using System.Security.Claims;
using Relia.Api.Contracts;
using Relia.Api.Errors;

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

public static class CurrentUserAccess
{
    /// <summary>
    /// Regla de lectura de un corresponsal: el admin lee cualquiera; el agente solo el suyo (403 si no,
    /// antes de mirar si existe, para no revelar qué ids existen).
    /// </summary>
    public static void EnsureCanReadAgent(this ICurrentUser user, Guid agentId)
    {
        if (user.Role == Role.Agent && user.AgentId != agentId) throw ApiErrors.Forbidden();
    }

    /// <summary>Panel admin: solo el admin.</summary>
    public static void EnsureAdmin(this ICurrentUser user)
    {
        if (user.Role != Role.Admin) throw ApiErrors.Forbidden();
    }

    /// <summary>
    /// Regla para operar (depósitos, retiros, cargas, ajustes): solo el corresponsal dueño. El admin lee
    /// todo pero no opera por el corresponsal.
    /// </summary>
    public static void EnsureCanOperateAgent(this ICurrentUser user, Guid agentId)
    {
        if (user.Role != Role.Agent || user.AgentId != agentId) throw ApiErrors.Forbidden();
    }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public Guid UserId => Guid.Parse(User.FindFirstValue(ReliaClaims.UserId)!);

    public Role Role => User.IsInRole(Roles.Admin) ? Role.Admin : Role.Agent;

    public Guid? AgentId =>
        Guid.TryParse(User.FindFirstValue(ReliaClaims.AgentId), out var id) ? id : null;
}
