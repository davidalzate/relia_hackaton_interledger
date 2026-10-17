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

    /// <summary>
    /// ¿Algún corresponsal tiene este celular o esta billetera? La billetera se compara en forma de payment
    /// pointer ($dominio/ruta), sin mayúsculas ni "/" final de ningún lado.
    /// </summary>
    Task<bool> UsesPhoneOrWalletAsync(string phone, string walletAddress, CancellationToken ct = default);

    /// <summary>Filas de v_admin_agent_summary con wallet_simulated, por nombre.</summary>
    Task<IReadOnlyList<AgentSummaryRow>> ListSummariesAsync(CancellationToken ct = default);
}

/// <summary>
/// Fila de v_admin_agent_summary. Los montos movidos son de depósitos y retiros completados, con el monto
/// solicitado (M); CommissionPaidToday usa "hoy" en America/Bogota.
/// </summary>
public record AgentSummaryRow
{
    public Guid AgentId { get; init; }
    public string Name { get; init; } = "";
    public string ManagerName { get; init; } = "";
    public string City { get; init; } = "";
    public string WalletAddress { get; init; } = "";
    public bool WalletSimulated { get; init; }
    public long Quota { get; init; }
    public long CashOnHand { get; init; }
    public long AccruedCommission { get; init; }
    public long SettledCommissionTotal { get; init; }
    public int OperationsCount { get; init; }
    public int DepositsCount { get; init; }
    public long DepositsValue { get; init; }
    public int WithdrawalsCount { get; init; }
    public long WithdrawalsValue { get; init; }
    public long TotalMoved { get; init; }
    public long CommissionPaidToday { get; init; }
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
    Task<WalletKey?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>¿Hay una llave con esta billetera? Payment pointer sin mayúsculas ni "/" final de ningún lado.</summary>
    Task<bool> IsWalletLinkedAsync(string walletAddress, CancellationToken ct = default);

    void Add(WalletKey key);
}

public interface IOtpChallengeRepository
{
    void Add(OtpChallenge challenge);

    /// <summary>SELECT ... FOR UPDATE del desafío, con los valores de la base. Solo dentro de una transacción.</summary>
    Task<OtpChallenge?> GetForUpdateAsync(Guid id, OtpPurpose purpose, CancellationToken ct = default);

    /// <summary>¿Se pidió otro código para el mismo celular y propósito después de este? ("Reenviar código").</summary>
    Task<bool> HasNewerAsync(OtpChallenge challenge, CancellationToken ct = default);
}

/// <summary>Cobro pendiente de aprobación y quién lo pide.</summary>
public record PendingApproval(Operation Operation, string AgentName, string ApprovalUrl, DateTimeOffset ExpiresAt);

/// <summary>Cupo y efectivo comprometidos por operaciones pendientes.</summary>
public record Reservations(long QuotaReserved, long CashReserved);

public interface IOperationRepository
{
    Task<Operation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Operation?> GetByIdempotencyKeyAsync(Guid agentId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Ids de las operaciones pendientes, la más vieja primero (para el barrido).</summary>
    Task<IReadOnlyList<Guid>> ListPendingIdsAsync(CancellationToken ct = default);

    /// <summary>SELECT ... FOR UPDATE de la operación, con los valores de la base. Solo dentro de una transacción.</summary>
    Task<Operation?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

    /// <summary>Cuándo se completó la primera operación del corresponsal; null si no tiene ninguna.</summary>
    Task<DateTimeOffset?> GetFirstCompletedAtAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>
    /// Retiros y cargas por Wallet pendientes cuyo pagador es <paramref name="phone"/> (el usuario del retiro o el
    /// corresponsal de la carga), con la approvalUrl de su último pago y su plazo después de <paramref name="now"/>.
    /// El más reciente primero.
    /// </summary>
    Task<IReadOnlyList<PendingApproval>> ListPendingApprovalsAsync(string phone, DateTimeOffset now,
        CancellationToken ct = default);

    /// <summary>Depósitos pendientes reservan cupo (cobrado); retiros pendientes reservan efectivo (neto).</summary>
    Task<Reservations> GetReservationsAsync(Guid agentId, CancellationToken ct = default);

    void Add(Operation operation);
    void AddEvent(OperationEvent operationEvent);
}

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Payment?> GetLatestByOperationAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Como <see cref="GetLatestByOperationAsync"/>, con FOR UPDATE y los valores de la base.</summary>
    Task<Payment?> GetLatestByOperationForUpdateAsync(Guid operationId, CancellationToken ct = default);
    Task<Payment?> GetLatestBySettlementAsync(Guid settlementId, CancellationToken ct = default);

    /// <summary>Cobros (incoming) pendientes que pasaron por el proxy: los que POST /demo/reset cancela en el riel.</summary>
    Task<IReadOnlyList<Payment>> ListPendingRailChargesAsync(CancellationToken ct = default);

    /// <summary>Como <see cref="GetLatestBySettlementAsync"/>, con FOR UPDATE y los valores de la base.</summary>
    Task<Payment?> GetLatestBySettlementForUpdateAsync(Guid settlementId, CancellationToken ct = default);
    void Add(Payment payment);
    void AddEvent(PaymentEvent paymentEvent);
}

public interface ILedgerRepository
{
    Task<LedgerEntry?> GetByOperationAsync(Guid operationId, CancellationToken ct = default);
    Task<LedgerEntry?> GetBySettlementAsync(Guid settlementId, CancellationToken ct = default);
    /// <summary>Último movimiento del corresponsal hasta <paramref name="atOrBefore"/>: el "último saldo conocido".</summary>
    Task<LedgerEntry?> GetLatestByAgentAsync(Guid agentId, DateTimeOffset atOrBefore, CancellationToken ct = default);

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
    /// <summary>Con el corresponsal y el admin que la creó.</summary>
    Task<Settlement?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>SELECT ... FOR UPDATE de la liquidación, con los valores de la base. Solo dentro de una transacción.</summary>
    Task<Settlement?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Settlement>> ListAsync(SettlementFilter filter, CancellationToken ct = default);

    /// <summary>Ids de las liquidaciones pendientes, la más vieja primero (para el barrido).</summary>
    Task<IReadOnlyList<Guid>> ListPendingIdsAsync(CancellationToken ct = default);

    Task<bool> HasPendingAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Última liquidación pagada del corresponsal: el período de la siguiente empieza al día siguiente.</summary>
    Task<Settlement?> GetLastPaidAsync(Guid agentId, CancellationToken ct = default);

    void Add(Settlement settlement);
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
