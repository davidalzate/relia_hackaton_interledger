using System.Globalization;
using Relia.Api.Contracts;
using Relia.Api.Errors;
using Relia.Api.Repositories;
using Relia.Api.Services.Support;
using Db = Relia.Api.Entities;

namespace Relia.Api.Services;

/// <summary>Historial unificado del corresponsal: operaciones, ajustes de efectivo y liquidaciones.</summary>
public interface IHistoryReader
{
    Task<HistoryPageDto> ReadAsync(Guid agentId, HistoryEntryKind? kind, OperationStatus? status, string? cursor,
        int limit, CancellationToken ct = default);
}

/// <summary>
/// Une las tres fuentes, la más reciente primero. El cursor es el instante (ticks UTC) de la última entrada
/// devuelta: la página siguiente trae las anteriores a él. Las entradas no completadas muestran antes = después
/// con el último saldo conocido a esa fecha (el "después" del último movimiento del libro anterior a ella).
/// </summary>
public class HistoryReader(IOperationRepository operations, ILedgerRepository ledger,
    ISettlementRepository settlements) : IHistoryReader
{
    private record Item(DateTime OccurredAt, Func<IReadOnlyList<Db.LedgerEntry>, HistoryEntryDto> Build);

    public async Task<HistoryPageDto> ReadAsync(Guid agentId, HistoryEntryKind? kind, OperationStatus? status,
        string? cursor, int limit, CancellationToken ct = default)
    {
        var before = ParseCursor(cursor);
        var items = new List<Item>();

        if (kind is null or HistoryEntryKind.Deposit or HistoryEntryKind.Withdrawal or HistoryEntryKind.QuotaTopUp)
        {
            var type = kind switch
            {
                HistoryEntryKind.Deposit => Db.OperationType.Deposit,
                HistoryEntryKind.Withdrawal => Db.OperationType.Withdrawal,
                HistoryEntryKind.QuotaTopUp => Db.OperationType.FloatLoad,
                _ => (Db.OperationType?)null,
            };
            var ops = await operations.ListByAgentAsync(agentId, type, status?.ToDb(), before, limit, ct);
            items.AddRange(ops.Select(o => new Item(o.CreatedAt, book => FromOperation(o, book))));
        }

        if ((kind is null or HistoryEntryKind.CashAdjustment) && (status is null or OperationStatus.Completed))
        {
            var adjustments = await ledger.ListCashAdjustmentsAsync(agentId, before, limit, ct);
            items.AddRange(adjustments.Select(a => new Item(a.CreatedAt, _ => FromAdjustment(a))));
        }

        if (kind is null or HistoryEntryKind.Settlement)
        {
            var list = await settlements.ListAsync(new SettlementFilter(null, null, agentId, null, before, limit), ct);
            items.AddRange(list.Where(s => status is null || s.Status.ToEntryStatus() == status)
                .Select(s => new Item(s.CreatedAt, book => FromSettlement(s, book))));
        }

        var page = items.OrderByDescending(i => i.OccurredAt).Take(limit).ToList();
        var book = page.Count == 0 ? [] : await ledger.ListByAgentAsync(agentId, ct);
        var next = page.Count == limit ? page[^1].OccurredAt.Ticks.ToString(CultureInfo.InvariantCulture) : null;
        return new HistoryPageDto(page.Select(i => i.Build(book)).ToList(), next);
    }

    private static DateTime? ParseCursor(string? cursor)
    {
        if (cursor is null) return null;
        if (!long.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            throw ApiErrors.Validation("cursor", "Cursor inválido");
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static HistoryEntryDto FromOperation(Db.Operation o, IReadOnlyList<Db.LedgerEntry> book)
    {
        var payment = o.LatestPayment();
        var entry = book.FirstOrDefault(l => l.OperationId == o.Id);
        var kind = o.Type switch
        {
            Db.OperationType.Deposit => HistoryEntryKind.Deposit,
            Db.OperationType.Withdrawal => HistoryEntryKind.Withdrawal,
            _ => HistoryEntryKind.QuotaTopUp,
        };
        return new HistoryEntryDto(o.Id.ToString(), kind, o.Reference(), Clock.Utc(o.CreatedAt), o.Status.ToApi(),
            o.RequestedAmount, entry?.ToChange() ?? LastKnown(book, o.CreatedAt),
            o.Status == Db.OperationStatus.Completed ? null : payment?.ErrorMessage,
            Mapping.ToFailureCode(payment?.ErrorCode), o.Id, null);
    }

    private static HistoryEntryDto FromAdjustment(Db.LedgerEntry a) =>
        new($"adj-{a.Id}", HistoryEntryKind.CashAdjustment, null, Clock.Utc(a.CreatedAt), OperationStatus.Completed,
            a.CashChange, a.ToChange(), a.Reason, null, null, null);

    private static HistoryEntryDto FromSettlement(Db.Settlement s, IReadOnlyList<Db.LedgerEntry> book)
    {
        var entry = book.FirstOrDefault(l => l.SettlementId == s.Id);
        return new HistoryEntryDto(s.Id.ToString(), HistoryEntryKind.Settlement, s.Reference(), Clock.Utc(s.CreatedAt),
            s.Status.ToEntryStatus(), s.Amount, entry?.ToChange() ?? LastKnown(book, s.CreatedAt), s.VoidReason,
            null, null, s.Id);
    }

    private static BalanceChangeDto LastKnown(IReadOnlyList<Db.LedgerEntry> book, DateTime at)
    {
        var last = book.LastOrDefault(l => l.CreatedAt <= at);
        return last is null
            ? Mapping.Unchanged(0, 0, 0)
            : Mapping.Unchanged(last.FloatAfter, last.CommissionAfter, last.CashAfter);
    }
}
