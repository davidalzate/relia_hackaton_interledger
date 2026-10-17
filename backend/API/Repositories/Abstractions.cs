using Relia.Api.Domain;

namespace Relia.Api.Repositories;

/// <summary>Confirma los cambios de los repositorios y abre transacciones.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Ejecuta <paramref name="work"/> en una transacción SQL; hace rollback si lanza. Si ya hay una
    /// transacción abierta, guarda dentro de ella y la deja para que la confirme quien la abrió.
    /// </summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}

public interface IAgentRepository
{
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Agent?> GetByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Lee la fila de agent_balances con SELECT ... FOR UPDATE, con los valores de la base aunque EF ya la
    /// tuviera en memoria. Solo dentro de una transacción. Null si el corresponsal no existe.
    /// </summary>
    Task<AgentBalance?> GetBalanceForUpdateAsync(Guid agentId, CancellationToken ct = default);
}

public interface IAdminRepository
{
    Task<Admin?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Admin?> GetByEmailAsync(string email, CancellationToken ct = default);
}

public interface IFeeRepository
{
    /// <summary>Tarifa vigente (la que no tiene vigente_hasta).</summary>
    Task<Fee> GetCurrentAsync(CancellationToken ct = default);
}

public interface IWalletKeyRepository
{
    Task<WalletKey?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    void Add(WalletKey key);
}

/// <summary>Cupo y efectivo comprometidos por operaciones pendientes.</summary>
public record Reservations(long QuotaReserved, long CashReserved);

public interface IOperationRepository
{
    Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Operation?> GetByIdempotencyKeyAsync(Guid agentId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Depósitos pendientes reservan cupo (cobrado); retiros pendientes reservan efectivo (neto).</summary>
    Task<Reservations> GetReservationsAsync(Guid agentId, CancellationToken ct = default);

    void Add(Operation operation);
    void AddEvent(OperationEvent operationEvent);
}

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Payment?> GetLatestByOperationAsync(Guid operationId, CancellationToken ct = default);
    Task<Payment?> GetLatestBySettlementAsync(Guid settlementId, CancellationToken ct = default);
    void Add(Payment payment);
    void AddEvent(PaymentEvent paymentEvent);
}

public interface ILedgerRepository
{
    Task<LedgerEntry?> GetByOperationAsync(Guid operationId, CancellationToken ct = default);
    Task<LedgerEntry?> GetBySettlementAsync(Guid settlementId, CancellationToken ct = default);
    Task<LedgerEntry?> GetLatestByAgentAsync(Guid agentId, CancellationToken ct = default);

    void Add(LedgerEntry entry);
}

/// <summary>Fila de la vista v_agent_history, con el id estable de la entrada y el motivo de fallo.</summary>
public record HistoryRow
{
    /// <summary>operation_id, settlement_id o "cash-{ledger_entry_id}".</summary>
    public string EntryId { get; init; } = "";

    /// <summary>Etiqueta de la base: deposit, withdrawal, float_load, cash_adjustment o settlement.</summary>
    public string Type { get; init; } = "";

    public string? Reference { get; init; }
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>operation_status como texto; las liquidaciones ya vienen traducidas por la vista.</summary>
    public string Status { get; init; } = "";

    public long Amount { get; init; }
    public long QuotaBefore { get; init; }
    public long QuotaAfter { get; init; }
    public long CommissionBefore { get; init; }
    public long CommissionAfter { get; init; }
    public long CashBefore { get; init; }
    public long CashAfter { get; init; }
    public string? Reason { get; init; }
    public string? FailureCode { get; init; }
    public Guid? OperationId { get; init; }
    public Guid? SettlementId { get; init; }
}

/// <summary>Posición de la última fila leída: la página siguiente empieza estrictamente después.</summary>
public record HistoryPosition(DateTimeOffset OccurredAt, string EntryId);

/// <summary>Filtros en etiquetas de la base (Type de <see cref="HistoryRow"/>, operation_status).</summary>
public record HistoryFilter(string? Type, string? Status, HistoryPosition? After, int Limit);

public interface IHistoryRepository
{
    /// <summary>Entradas del corresponsal, la más reciente primero (occurred_at, luego EntryId).</summary>
    Task<IReadOnlyList<HistoryRow>> ListAsync(Guid agentId, HistoryFilter filter, CancellationToken ct = default);
}

public interface IReceiptRepository
{
    Task<Receipt?> GetByOperationAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Agrega una emisión; el trigger de la base decide si es copia.</summary>
    void AddIssue(ReceiptIssue issue);
}

/// <summary>
/// <paramref name="CreatedFrom"/> y <paramref name="CreatedTo"/> son instantes UTC; el servicio convierte las
/// fechas del API (días en America/Bogota) a ese rango.
/// </summary>
public record SettlementFilter(
    DateTimeOffset? CreatedFrom, DateTimeOffset? CreatedTo, Guid? AgentId, SettlementState? State,
    DateTimeOffset? Before, int Limit);

public interface ISettlementRepository
{
    Task<Settlement?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Settlement>> ListAsync(SettlementFilter filter, CancellationToken ct = default);
}

public interface ISimulatedMessageRepository
{
    void Add(SimulatedMessage message);

    /// <summary>
    /// El más reciente primero (id descendente). <paramref name="phone"/> null = todos los celulares;
    /// <paramref name="beforeId"/> = id del último mensaje de la página anterior.
    /// </summary>
    Task<IReadOnlyList<SimulatedMessage>> ListAsync(string? phone, long? beforeId, int limit,
        CancellationToken ct = default);

    Task<int> CountUnreadAsync(string? phone, CancellationToken ct = default);

    /// <summary>Marca como leídos los no leídos del celular, o de todos si es null. Escribe de inmediato.</summary>
    Task MarkReadAsync(string? phone, CancellationToken ct = default);
}
