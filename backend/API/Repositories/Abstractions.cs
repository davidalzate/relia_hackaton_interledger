using Relia.Api.Entities;

namespace Relia.Api.Repositories;

// Fechas: DateTime en UTC (Kind = Utc), como las genera el scaffold para timestamptz.

/// <summary>Confirma los cambios de los repositorios y abre transacciones.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Ejecuta <paramref name="work"/> en una transacción SQL; hace rollback si lanza.</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}

public interface IAgentRepository
{
    /// <summary>Incluye AgentBalance.</summary>
    Task<Agent?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Agent?> GetByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Lee la fila de agent_balances con SELECT ... FOR UPDATE. Solo dentro de una transacción.</summary>
    Task<AgentBalance> GetBalanceForUpdateAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Todos los corresponsales con sus saldos, por nombre.</summary>
    Task<IReadOnlyList<Agent>> ListAsync(CancellationToken ct = default);

    Task<Agent?> GetByPhoneAsync(string phone, CancellationToken ct = default);
    Task<bool> AnyWithPaymentPointerAsync(string pointer, CancellationToken ct = default);
}

public interface IAdminRepository
{
    Task<Admin?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Admin?> GetByEmailAsync(string email, CancellationToken ct = default);
}

public interface IFeeRepository
{
    /// <summary>Tarifa vigente (la que no tiene valid_until).</summary>
    Task<Fee> GetCurrentAsync(CancellationToken ct = default);
}

public interface IWalletKeyRepository
{
    Task<WalletKey?> GetByMobileAsync(string mobile, CancellationToken ct = default);
    Task<bool> AnyWithPaymentPointerAsync(string pointer, CancellationToken ct = default);
    void Add(WalletKey key);
}

public interface IOtpChallengeRepository
{
    Task<OtpChallenge?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Desafíos de vinculación sin verificar del celular (para invalidarlos al reenviar).</summary>
    Task<IReadOnlyList<OtpChallenge>> ListOpenAsync(string mobile, CancellationToken ct = default);

    void Add(OtpChallenge challenge);
}

/// <summary>Cupo y efectivo comprometidos por operaciones pendientes.</summary>
public record Reservations(long FloatReserved, long CashReserved);

public interface IOperationRepository
{
    /// <summary>Incluye WalletKey.</summary>
    Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Operation?> GetByIdempotencyKeyAsync(Guid agentId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Depósitos pendientes reservan cupo (cobrado); retiros pendientes reservan efectivo (neto).</summary>
    Task<Reservations> GetReservationsAsync(Guid agentId, CancellationToken ct = default);

    Task<IReadOnlyList<Operation>> ListByAgentAsync(Guid agentId, OperationType? type, OperationStatus? status,
        DateTime? before, int limit, CancellationToken ct = default);

    /// <summary>Depósitos y retiros completados por corresponsal y tipo: cantidad y suma del monto solicitado.</summary>
    Task<IReadOnlyList<CompletedTotals>> GetCompletedTotalsAsync(CancellationToken ct = default);

    /// <summary>Operaciones pendientes (para el barrido y "Mi billetera"). Incluye Agent, WalletKey y Payments.</summary>
    Task<IReadOnlyList<Operation>> ListPendingAsync(CancellationToken ct = default);

    Task<DateTime?> GetFirstOperationDateAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Estado actual en la base, sin pasar por el seguimiento de EF (para no completar dos veces).</summary>
    Task<OperationStatus?> GetFreshStatusAsync(Guid id, CancellationToken ct = default);

    /// <summary>Vuelve a leer la operación y sus pagos desde la base.</summary>
    Task ReloadAsync(Operation operation, CancellationToken ct = default);

    /// <summary>Eventos de la operación, en orden.</summary>
    Task<IReadOnlyList<OperationEvent>> ListEventsAsync(Guid operationId, CancellationToken ct = default);

    void Add(Operation operation);
    void AddEvent(OperationEvent operationEvent);
}

public record CompletedTotals(Guid AgentId, OperationType Type, int Count, long Amount);

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

    Task<IReadOnlyList<LedgerEntry>> ListCashAdjustmentsAsync(Guid agentId, DateTime? before, int limit,
        CancellationToken ct = default);

    /// <summary>Todo el libro del corresponsal, en orden de inserción.</summary>
    Task<IReadOnlyList<LedgerEntry>> ListByAgentAsync(Guid agentId, CancellationToken ct = default);

    void Add(LedgerEntry entry);
}

public interface IReceiptRepository
{
    Task<Receipt?> GetByOperationAsync(Guid operationId, CancellationToken ct = default);

    void Add(Receipt receipt);

    Task<int> CountIssuancesAsync(Guid receiptId, CancellationToken ct = default);

    /// <summary>Agrega una emisión; el trigger de la base decide si es copia.</summary>
    void AddIssuance(ReceiptIssuance issuance);
}

/// <summary>
/// <paramref name="CreatedFrom"/> y <paramref name="CreatedTo"/> son instantes UTC; el servicio convierte las
/// fechas del API (días en America/Bogota) a ese rango.
/// </summary>
public record SettlementFilter(
    DateTime? CreatedFrom, DateTime? CreatedTo, Guid? AgentId, SettlementStatus? Status,
    DateTime? Before, int Limit);

public interface ISettlementRepository
{
    /// <summary>Incluye Agent y CreatedByAdmin. Solo lectura.</summary>
    Task<Settlement?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Settlement>> ListAsync(SettlementFilter filter, CancellationToken ct = default);

    /// <summary>Con seguimiento de cambios, Agent y Payments: para hacer avanzar la liquidación.</summary>
    Task<Settlement?> GetTrackedAsync(Guid id, CancellationToken ct = default);

    Task<SettlementStatus?> GetFreshStatusAsync(Guid id, CancellationToken ct = default);

    Task<bool> HasPendingAsync(Guid agentId, CancellationToken ct = default);

    Task<Settlement?> GetLastPaidAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Pendientes, con seguimiento (para el barrido).</summary>
    Task<IReadOnlyList<Settlement>> ListPendingTrackedAsync(CancellationToken ct = default);

    void Add(Settlement settlement);

    /// <summary>Suma pagada por corresponsal con paid_at desde <paramref name="paidFromUtc"/>.</summary>
    Task<IReadOnlyDictionary<Guid, long>> SumPaidSinceAsync(DateTime paidFromUtc, CancellationToken ct = default);
}

public interface ISimulatedMessageRepository
{
    void Add(SimulatedMessage message);

    /// <summary>La más reciente primero. <paramref name="beforeId"/> pagina hacia atrás.</summary>
    Task<IReadOnlyList<SimulatedMessage>> ListAsync(string? phone, long? beforeId, int limit, CancellationToken ct = default);

    Task<int> CountUnreadAsync(string? phone, CancellationToken ct = default);

    /// <summary>Marca como leídos los mensajes del celular (todos si es null). Falta guardar.</summary>
    Task MarkReadAsync(string? phone, CancellationToken ct = default);
}
