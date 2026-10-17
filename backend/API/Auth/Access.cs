using Relia.Api.Contracts;
using Relia.Api.Errors;

namespace Relia.Api.Auth;

/// <summary>Reglas de rol y propiedad: el agent opera solo sobre su corresponsal; el admin lee todo y no opera.</summary>
public static class Access
{
    /// <summary>El admin lee cualquier corresponsal; el agent solo el suyo (403 si no).</summary>
    public static void EnsureCanRead(this ICurrentUser user, Guid agentId)
    {
        if (user.Role == Role.Agent && user.AgentId != agentId) throw ApiErrors.Forbidden();
    }

    /// <summary>Operar (crear, cancelar, confirmar) es solo del agent dueño. El admin recibe 403.</summary>
    public static void EnsureCanOperate(this ICurrentUser user, Guid agentId)
    {
        if (user.Role != Role.Agent || user.AgentId != agentId) throw ApiErrors.Forbidden();
    }
}
