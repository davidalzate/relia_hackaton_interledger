using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Relia.Api.Domain;

namespace Relia.Api.Persistence;

/// <summary>
/// Mapeo de EF Core al esquema docs/relia_schema_hackathon_v0.2.sql. El esquema lo crea ese script,
/// no EF: no hay migraciones. Triggers, CHECK y enums viven en la base.
/// </summary>
public class ReliaDbContext(DbContextOptions<ReliaDbContext> options) : DbContext(options)
{
    public DbSet<Fee> Fees => Set<Fee>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<AgentBalance> AgentBalances => Set<AgentBalance>();
    public DbSet<WalletKey> WalletKeys => Set<WalletKey>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<SimulatedMessage> SimulatedMessages => Set<SimulatedMessage>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<OperationEvent> OperationEvents => Set<OperationEvent>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptIssue> ReceiptIssues => Set<ReceiptIssue>();

    /// <summary>Registra los enums de PostgreSQL. Se llama desde UseNpgsql.</summary>
    public static void MapEnums(NpgsqlDbContextOptionsBuilder o)
    {
        o.MapEnum<OperationKind>("tipo_operacion");
        o.MapEnum<TopUpMethodKind>("metodo_carga");
        o.MapEnum<OperationState>("estado_operacion");
        o.MapEnum<SettlementState>("estado_liquidacion");
        o.MapEnum<PaymentRail>("riel_pago");
        o.MapEnum<PaymentDirection>("direccion_pago");
        o.MapEnum<MessageChannel>("canal_mensaje");
        o.MapEnum<MessageKind>("tipo_mensaje");
        o.MapEnum<LedgerEntryKind>("tipo_movimiento");
        o.MapEnum<OtpPurpose>("proposito_otp");
        o.MapEnum<ReceiptChannel>("canal_emision");
        o.MapEnum<ReceiptRequester>("solicitante_recibo");
        o.MapEnum<EventOrigin>("origen_evento");
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Fee>(e =>
        {
            e.ToTable("tarifas");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Total).HasColumnName("comision_total");
            e.Property(x => x.ReliaShare).HasColumnName("comision_relia");
            e.Property(x => x.AgentShare).HasColumnName("comision_corresponsal");
            e.Property(x => x.ValidFrom).HasColumnName("vigente_desde").HasDefaultValueSql("now()");
            e.Property(x => x.ValidTo).HasColumnName("vigente_hasta");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<Agent>(e =>
        {
            e.ToTable("corresponsales");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Email).HasColumnName("correo");
            e.Property(x => x.Name).HasColumnName("nombre");
            e.Property(x => x.ManagerName).HasColumnName("responsable");
            e.Property(x => x.City).HasColumnName("ciudad");
            e.Property(x => x.Address).HasColumnName("direccion");
            e.Property(x => x.Phone).HasColumnName("telefono");
            e.Property(x => x.WalletAddress).HasColumnName("payment_pointer");
            e.Property(x => x.Password).HasColumnName("clave");
            e.Property(x => x.Active).HasColumnName("activo");
            CreatedAt(e.Property(x => x.CreatedAt));
            UpdatedAt(e.Property(x => x.UpdatedAt));
            e.HasOne(x => x.Balance).WithOne().HasForeignKey<AgentBalance>(x => x.AgentId);
        });

        b.Entity<Admin>(e =>
        {
            e.ToTable("admins");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Email).HasColumnName("correo");
            e.Property(x => x.Name).HasColumnName("nombre");
            e.Property(x => x.Password).HasColumnName("clave");
            e.Property(x => x.Active).HasColumnName("activo");
            CreatedAt(e.Property(x => x.CreatedAt));
            UpdatedAt(e.Property(x => x.UpdatedAt));
        });

        b.Entity<AgentBalance>(e =>
        {
            e.ToTable("cupos");
            e.HasKey(x => x.AgentId);
            e.Property(x => x.AgentId).HasColumnName("corresponsal_id");
            e.Property(x => x.Quota).HasColumnName("cupo_actual");
            e.Property(x => x.AccruedCommission).HasColumnName("comision_acumulada");
            e.Property(x => x.SettledCommissionTotal).HasColumnName("comision_liquidada_total");
            e.Property(x => x.CashOnHand).HasColumnName("efectivo_actual");
            UpdatedAt(e.Property(x => x.UpdatedAt));
        });

        b.Entity<WalletKey>(e =>
        {
            e.ToTable("llaves_wallet");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Phone).HasColumnName("celular");
            e.Property(x => x.WalletAddress).HasColumnName("payment_pointer");
            e.Property(x => x.WalletAddressUrl).HasColumnName("wallet_address_url")
                .HasComputedColumnSql("'https://' || substr(payment_pointer, 2)", stored: true);
            e.Property(x => x.LinkedAt).HasColumnName("vinculada_en").HasDefaultValueSql("now()");
        });

        b.Entity<OtpChallenge>(e =>
        {
            e.ToTable("desafios_otp");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Phone).HasColumnName("celular");
            e.Property(x => x.Purpose).HasColumnName("proposito");
            e.Property(x => x.WalletAddress).HasColumnName("payment_pointer");
            e.Property(x => x.CodeHash).HasColumnName("codigo_hash");
            e.Property(x => x.Attempts).HasColumnName("intentos");
            e.Property(x => x.MaxAttempts).HasColumnName("max_intentos");
            e.Property(x => x.ExpiresAt).HasColumnName("expira_en");
            e.Property(x => x.VerifiedAt).HasColumnName("verificado_en");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<Operation>(e =>
        {
            e.ToTable("operaciones");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Number).HasColumnName("numero").UseIdentityAlwaysColumn();
            e.Property(x => x.Reference).HasColumnName("numero_visible")
                .HasComputedColumnSql("'OP-' || lpad(numero::text, 6, '0')", stored: true);
            e.Property(x => x.AgentId).HasColumnName("corresponsal_id");
            e.Property(x => x.Kind).HasColumnName("tipo");
            e.Property(x => x.TopUpMethod).HasColumnName("metodo_carga");
            e.Property(x => x.WalletKeyId).HasColumnName("llave_wallet_id");
            e.Property(x => x.RequestedAmount).HasColumnName("monto_solicitado");
            e.Property(x => x.RoundAmount).HasColumnName("valor_redondo");
            e.Property(x => x.FeeId).HasColumnName("tarifa_id");
            e.Property(x => x.FeeTotal).HasColumnName("comision_total");
            e.Property(x => x.FeeRelia).HasColumnName("comision_relia");
            e.Property(x => x.FeeAgent).HasColumnName("comision_corresponsal");
            e.Property(x => x.NetAmount).HasColumnName("monto_neto");
            e.Property(x => x.ChargedAmount).HasColumnName("monto_cobrado");
            e.Property(x => x.State).HasColumnName("estado");
            e.Property(x => x.IdempotencyKey).HasColumnName("clave_idempotencia");
            CreatedAt(e.Property(x => x.CreatedAt));
            e.Property(x => x.CompletedAt).HasColumnName("completada_en");
            UpdatedAt(e.Property(x => x.UpdatedAt));
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId);
            e.HasOne(x => x.WalletKey).WithMany().HasForeignKey(x => x.WalletKeyId);
        });

        b.Entity<SimulatedMessage>(e =>
        {
            e.ToTable("mensajes_simulados");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Channel).HasColumnName("canal");
            e.Property(x => x.Kind).HasColumnName("tipo");
            e.Property(x => x.Recipient).HasColumnName("destinatario");
            e.Property(x => x.Content).HasColumnName("contenido");
            e.Property(x => x.Link).HasColumnName("enlace");
            e.Property(x => x.OperationId).HasColumnName("operacion_id");
            e.Property(x => x.Read).HasColumnName("leido");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<Settlement>(e =>
        {
            e.ToTable("liquidaciones");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Number).HasColumnName("numero").UseIdentityAlwaysColumn();
            e.Property(x => x.Reference).HasColumnName("numero_visible")
                .HasComputedColumnSql("'LQ-' || lpad(numero::text, 6, '0')", stored: true);
            e.Property(x => x.AgentId).HasColumnName("corresponsal_id");
            e.Property(x => x.CreatedByAdminId).HasColumnName("creada_por_admin_id");
            e.Property(x => x.DestinationWalletAddress).HasColumnName("payment_pointer_destino");
            e.Property(x => x.PeriodFrom).HasColumnName("periodo_desde");
            e.Property(x => x.PeriodTo).HasColumnName("periodo_hasta");
            e.Property(x => x.Amount).HasColumnName("monto");
            e.Property(x => x.State).HasColumnName("estado");
            e.Property(x => x.PaidAt).HasColumnName("pagada_en");
            e.Property(x => x.VoidReason).HasColumnName("motivo_anulacion");
            CreatedAt(e.Property(x => x.CreatedAt));
            UpdatedAt(e.Property(x => x.UpdatedAt));
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId);
            e.HasOne(x => x.CreatedByAdmin).WithMany().HasForeignKey(x => x.CreatedByAdminId);
        });

        b.Entity<Payment>(e =>
        {
            e.ToTable("pagos");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.OperationId).HasColumnName("operacion_id");
            e.Property(x => x.SettlementId).HasColumnName("liquidacion_id");
            e.Property(x => x.Rail).HasColumnName("riel");
            e.Property(x => x.Direction).HasColumnName("direccion");
            e.Property(x => x.State).HasColumnName("estado");
            e.Property(x => x.Amount).HasColumnName("monto");
            e.Property(x => x.Currency).HasColumnName("moneda").HasColumnType("char(3)");
            e.Property(x => x.SourceWalletAddress).HasColumnName("payment_pointer_origen");
            e.Property(x => x.DestinationWalletAddress).HasColumnName("payment_pointer_destino");
            e.Property(x => x.IncomingPaymentUrl).HasColumnName("op_incoming_payment_url");
            e.Property(x => x.QuoteUrl).HasColumnName("op_quote_url");
            e.Property(x => x.OutgoingPaymentUrl).HasColumnName("op_outgoing_payment_url");
            e.Property(x => x.ApprovalUrl).HasColumnName("url_aprobacion");
            e.Property(x => x.GrantContinueUri).HasColumnName("grant_continue_uri");
            e.Property(x => x.GrantContinueTokenEncrypted).HasColumnName("grant_continue_token_cifrado");
            e.Property(x => x.RailReference).HasColumnName("referencia_riel");
            e.Property(x => x.DebitValue).HasColumnName("monto_debito_valor");
            e.Property(x => x.DebitAssetCode).HasColumnName("monto_debito_codigo");
            e.Property(x => x.DebitAssetScale).HasColumnName("monto_debito_escala");
            e.Property(x => x.ReceivedValue).HasColumnName("monto_recibido_valor");
            e.Property(x => x.ReceivedAssetCode).HasColumnName("monto_recibido_codigo");
            e.Property(x => x.ReceivedAssetScale).HasColumnName("monto_recibido_escala");
            e.Property(x => x.ExpiresAt).HasColumnName("vence_en");
            e.Property(x => x.ErrorCode).HasColumnName("error_codigo");
            e.Property(x => x.ErrorMessage).HasColumnName("error_mensaje");
            e.Property(x => x.RailDetail).HasColumnName("detalle_riel").HasColumnType("jsonb");
            CreatedAt(e.Property(x => x.CreatedAt));
            e.Property(x => x.CompletedAt).HasColumnName("completado_en");
            UpdatedAt(e.Property(x => x.UpdatedAt));
        });

        b.Entity<PaymentEvent>(e =>
        {
            e.ToTable("eventos_pago");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.PaymentId).HasColumnName("pago_id");
            e.Property(x => x.Action).HasColumnName("accion");
            e.Property(x => x.PreviousState).HasColumnName("estado_anterior");
            e.Property(x => x.NewState).HasColumnName("estado_nuevo");
            e.Property(x => x.Origin).HasColumnName("origen");
            e.Property(x => x.HttpStatus).HasColumnName("codigo_http");
            e.Property(x => x.RailResponse).HasColumnName("respuesta_riel").HasColumnType("jsonb");
            e.Property(x => x.Detail).HasColumnName("detalle");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<OperationEvent>(e =>
        {
            e.ToTable("eventos_operacion");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.OperationId).HasColumnName("operacion_id");
            e.Property(x => x.PreviousState).HasColumnName("estado_anterior");
            e.Property(x => x.NewState).HasColumnName("estado_nuevo");
            e.Property(x => x.Origin).HasColumnName("origen");
            e.Property(x => x.Detail).HasColumnName("detalle");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("movimientos_cupo");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.AgentId).HasColumnName("corresponsal_id");
            e.Property(x => x.Kind).HasColumnName("tipo");
            e.Property(x => x.OperationId).HasColumnName("operacion_id");
            e.Property(x => x.SettlementId).HasColumnName("liquidacion_id");
            e.Property(x => x.PaymentId).HasColumnName("pago_id");
            e.Property(x => x.QuotaBefore).HasColumnName("cupo_antes");
            e.Property(x => x.QuotaChange).HasColumnName("variacion_cupo");
            e.Property(x => x.QuotaAfter).HasColumnName("cupo_despues");
            e.Property(x => x.CommissionBefore).HasColumnName("comision_antes");
            e.Property(x => x.CommissionChange).HasColumnName("variacion_comision");
            e.Property(x => x.CommissionAfter).HasColumnName("comision_despues");
            e.Property(x => x.CashBefore).HasColumnName("efectivo_antes");
            e.Property(x => x.CashChange).HasColumnName("variacion_efectivo");
            e.Property(x => x.CashAfter).HasColumnName("efectivo_despues");
            e.Property(x => x.Reason).HasColumnName("motivo");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<Receipt>(e =>
        {
            e.ToTable("recibos");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.OperationId).HasColumnName("operacion_id");
            e.Property(x => x.Code).HasColumnName("codigo")
                .HasDefaultValueSql("upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 12))");
            e.Property(x => x.Data).HasColumnName("datos").HasColumnType("jsonb");
            e.Property(x => x.ContentHash).HasColumnName("hash_contenido");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<ReceiptIssue>(e =>
        {
            e.ToTable("emisiones_recibo");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.ReceiptId).HasColumnName("recibo_id");
            e.Property(x => x.Channel).HasColumnName("canal");
            e.Property(x => x.RequestedBy).HasColumnName("solicitado_por");
            e.Property(x => x.AgentId).HasColumnName("corresponsal_id");
            e.Property(x => x.CustomerPhone).HasColumnName("celular_usuario");
            // Lo calcula el trigger marcar_copia_recibo al insertar.
            e.Property(x => x.IsCopy).HasColumnName("es_copia").ValueGeneratedOnAdd();
            CreatedAt(e.Property(x => x.CreatedAt));
        });
    }

    private static void UuidKey(PropertyBuilder<Guid> p) =>
        p.HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

    private static void CreatedAt(PropertyBuilder<DateTimeOffset> p) =>
        p.HasColumnName("creado_en").HasDefaultValueSql("now()");

    // Lo mantiene el trigger set_actualizado_en.
    private static void UpdatedAt(PropertyBuilder<DateTimeOffset> p) =>
        p.HasColumnName("actualizado_en").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
}
