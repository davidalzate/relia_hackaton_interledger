using Relia.Api.Contracts;
using Relia.Api.Domain;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>De entidades de la base a DTOs del contrato. Compartido por los servicios.</summary>
public static class Mapping
{
    /// <summary>Requiere el corresponsal con Balance cargado.</summary>
    public static AgentDto ToDto(this Agent agent, Reservations reservations)
    {
        var balance = agent.Balance
                      ?? throw new InvalidOperationException($"El corresponsal {agent.Id} no tiene agent_balances");
        return new AgentDto(
            agent.Id, agent.Name, agent.ManagerName, agent.City, agent.Address, agent.Phone, agent.WalletAddress,
            new AgentBalancesDto(
                balance.Quota, reservations.QuotaReserved, balance.CashOnHand, reservations.CashReserved,
                balance.AccruedCommission, balance.SettledCommissionTotal));
    }
}
