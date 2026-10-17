using Microsoft.Extensions.Options;
using Relia.Api.Contracts;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services;

/// <summary>Ticket 14: panel admin. El rol se exige en el controlador.</summary>
public interface IAdminService
{
    /// <summary>Filas y totales de la red en una llamada. Solo depósitos y retiros completados, con el monto solicitado.</summary>
    Task<AgentListDto> ListAgentsAsync(CancellationToken ct = default);
}

public class AdminService(
    IAgentRepository agents, IOperationRepository operations, ISettlementRepository settlements,
    IOptions<ReliaOptions> relia, TimeProvider time) : IAdminService
{
    public async Task<AgentListDto> ListAgentsAsync(CancellationToken ct = default)
    {
        var list = await agents.ListAsync(ct);
        var totals = await operations.GetCompletedTotalsAsync(ct);
        var paidToday = await settlements.SumPaidSinceAsync(Clock.StartOfBogotaDayUtc(time.BogotaToday()), ct);

        var items = list.Select(agent =>
        {
            var deposits = Totals(totals, agent.Id, Db.OperationType.Deposit);
            var withdrawals = Totals(totals, agent.Id, Db.OperationType.Withdrawal);
            var balance = agent.AgentBalance ?? new Db.AgentBalance();
            return new AgentSummaryDto(agent.Id, agent.Name, agent.ContactPerson, agent.City, agent.PaymentPointer,
                IsSimulated(agent.PaymentPointer), deposits.Count + withdrawals.Count, deposits, withdrawals,
                deposits.Amount + withdrawals.Amount, balance.FloatBalance, balance.ExpectedCash,
                balance.AccruedCommission, balance.SettledCommissionTotal, paidToday.GetValueOrDefault(agent.Id));
        }).ToList();

        var allDeposits = Sum(items.Select(i => i.Deposits));
        var allWithdrawals = Sum(items.Select(i => i.Withdrawals));
        return new AgentListDto(items, new NetworkTotalsDto(
            new CountAmountDto(allDeposits.Count + allWithdrawals.Count, allDeposits.Amount + allWithdrawals.Amount),
            allDeposits, allWithdrawals,
            items.Sum(i => i.AccruedCommission), items.Sum(i => i.CommissionPaidToday)));
    }

    public bool IsSimulated(string pointer) => pointer.StartsWith(relia.Value.SimulatedWalletPrefix, StringComparison.Ordinal);

    private static CountAmountDto Totals(IReadOnlyList<CompletedTotals> totals, Guid agentId, Db.OperationType type) =>
        totals.FirstOrDefault(t => t.AgentId == agentId && t.Type == type) is { } t
            ? new CountAmountDto(t.Count, t.Amount)
            : new CountAmountDto(0, 0);

    private static CountAmountDto Sum(IEnumerable<CountAmountDto> values) =>
        values.Aggregate(new CountAmountDto(0, 0), (a, b) => new CountAmountDto(a.Count + b.Count, a.Amount + b.Amount));
}
