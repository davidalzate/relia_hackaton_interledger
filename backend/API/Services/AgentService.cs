using Relia.Api.Auth;
using Relia.Api.Contracts;
using Db = Relia.Api.Entities;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>Ticket 04: perfil, saldos e historial del corresponsal.</summary>
public class AgentService(
    IAgentRepository agents, IOperationRepository operations, IHistoryReader history, ICurrentUser currentUser)
    : IAgentService
{
    public async Task<AgentDto> GetAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        currentUser.EnsureCanRead(agentId);
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        var reservations = await operations.GetReservationsAsync(agentId, ct);
        return ToDto(agent, reservations);
    }

    public async Task<HistoryPageDto> GetHistoryAsync(Guid agentId, HistoryEntryKind? kind, OperationStatus? status,
        string? cursor, int limit, CancellationToken ct = default)
    {
        currentUser.EnsureCanRead(agentId);
        _ = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        return await history.ReadAsync(agentId, kind, status, cursor, limit, ct);
    }

    public static AgentDto ToDto(Db.Agent agent, Reservations reservations)
    {
        var balance = agent.AgentBalance ?? new Db.AgentBalance { AgentId = agent.Id };
        return new AgentDto(agent.Id, agent.Name, agent.ContactPerson, agent.City, agent.Address, agent.Phone,
            agent.PaymentPointer,
            new AgentBalancesDto(balance.FloatBalance, reservations.FloatReserved, balance.ExpectedCash,
                reservations.CashReserved, balance.AccruedCommission, balance.SettledCommissionTotal));
    }
}
