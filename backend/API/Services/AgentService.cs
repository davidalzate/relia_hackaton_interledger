using System.Globalization;
using System.Text;
using Relia.Api.Auth;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;

namespace Relia.Api.Services;

/// <summary>GET /agents/{agentId} y GET /agents/{agentId}/history.</summary>
public class AgentService(
    IAgentRepository agents, IOperationRepository operations, IHistoryRepository history, ICurrentUser currentUser)
    : IAgentService
{
    // Etiquetas de v_agent_history: el tipo de la carga de cupo en la base es float_load.
    private static readonly Dictionary<HistoryEntryKind, string> KindLabels = new()
    {
        [HistoryEntryKind.Deposit] = "deposit",
        [HistoryEntryKind.Withdrawal] = "withdrawal",
        [HistoryEntryKind.QuotaTopUp] = "float_load",
        [HistoryEntryKind.CashAdjustment] = "cash_adjustment",
        [HistoryEntryKind.Settlement] = "settlement",
    };

    private static readonly Dictionary<string, HistoryEntryKind> KindsByLabel =
        KindLabels.ToDictionary(p => p.Value, p => p.Key);

    public async Task<AgentDto> GetAgentAsync(Guid agentId, CancellationToken ct = default)
    {
        currentUser.EnsureCanReadAgent(agentId);
        var agent = await agents.GetByIdAsync(agentId, ct) ?? throw ApiErrors.NotFound();
        var reservations = await operations.GetReservationsAsync(agentId, ct);
        return agent.ToDto(reservations);
    }

    public async Task<HistoryPageDto> GetHistoryAsync(Guid agentId, HistoryEntryKind? kind, OperationStatus? status,
        string? cursor, int limit, CancellationToken ct = default)
    {
        currentUser.EnsureCanReadAgent(agentId);
        if (await agents.GetByIdAsync(agentId, ct) is null) throw ApiErrors.NotFound();

        // Se pide una fila de más para saber si hay página siguiente.
        var filter = new HistoryFilter(
            kind is null ? null : KindLabels[kind.Value],
            status?.ToString().ToLowerInvariant(),
            cursor is null ? null : HistoryCursor.Decode(cursor),
            limit + 1);
        var rows = await history.ListAsync(agentId, filter, ct);

        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? HistoryCursor.Encode(new HistoryPosition(page[^1].OccurredAt, page[^1].EntryId)) : null;
        return new HistoryPageDto(page.Select(ToDto).ToList(), next);
    }

    private static HistoryEntryDto ToDto(HistoryRow row) => new(
        row.EntryId,
        KindsByLabel[row.Type],
        row.Reference,
        row.OccurredAt,
        Enum.Parse<OperationStatus>(row.Status, ignoreCase: true),
        row.Amount,
        new BalanceChangeDto(
            new BalancePairDto(row.QuotaBefore, row.QuotaAfter),
            new BalancePairDto(row.CommissionBefore, row.CommissionAfter),
            new BalancePairDto(row.CashBefore, row.CashAfter)),
        row.Reason,
        row.FailureCode is null ? null : Mapping.FailureCodes[row.FailureCode],
        row.OperationId,
        row.SettlementId);

    /// <summary>Cursor opaco: base64url de "{ticks UTC}|{entryId}" de la última fila de la página.</summary>
    private static class HistoryCursor
    {
        public static string Encode(HistoryPosition position)
        {
            var raw = $"{position.OccurredAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{position.EntryId}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static HistoryPosition Decode(string cursor)
        {
            try
            {
                var base64 = cursor.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|', 2);
                if (parts.Length != 2 || parts[1].Length == 0) throw new FormatException();
                var ticks = long.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
                return new HistoryPosition(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
            }
            catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
            {
                throw ApiErrors.Validation("cursor", "Cursor inválido.");
            }
        }
    }
}
