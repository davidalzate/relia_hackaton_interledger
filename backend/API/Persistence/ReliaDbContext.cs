using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Relia.Api.Entities;

namespace Relia.Api.Persistence;

public partial class ReliaDbContext : DbContext
{
    public ReliaDbContext(DbContextOptions<ReliaDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Admin> Admins { get; set; }

    public virtual DbSet<Agent> Agents { get; set; }

    public virtual DbSet<AgentBalance> AgentBalances { get; set; }

    public virtual DbSet<Fee> Fees { get; set; }

    public virtual DbSet<LedgerEntry> LedgerEntries { get; set; }

    public virtual DbSet<Operation> Operations { get; set; }

    public virtual DbSet<OperationEvent> OperationEvents { get; set; }

    public virtual DbSet<OtpChallenge> OtpChallenges { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<PaymentEvent> PaymentEvents { get; set; }

    public virtual DbSet<Receipt> Receipts { get; set; }

    public virtual DbSet<ReceiptIssuance> ReceiptIssuances { get; set; }

    public virtual DbSet<Settlement> Settlements { get; set; }

    public virtual DbSet<SimulatedMessage> SimulatedMessages { get; set; }

    public virtual DbSet<VAdminAgentSummary> VAdminAgentSummaries { get; set; }

    public virtual DbSet<VAgentBalanceCheck> VAgentBalanceChecks { get; set; }

    public virtual DbSet<VAgentHistory> VAgentHistories { get; set; }

    public virtual DbSet<VWalletTraceability> VWalletTraceabilities { get; set; }

    public virtual DbSet<WalletKey> WalletKeys { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum("event_source", new[] { "agent", "system", "rail", "simulator" })
            .HasPostgresEnum("ledger_entry_type", new[] { "deposit", "withdrawal", "float_load", "settlement", "cash_adjustment" })
            .HasPostgresEnum("load_method", new[] { "bre_b", "wallet" })
            .HasPostgresEnum("message_channel", new[] { "sms", "whatsapp" })
            .HasPostgresEnum("message_type", new[] { "otp", "wallet_invitation", "deposit_notice", "payment_request", "receipt", "commission_notice" })
            .HasPostgresEnum("operation_status", new[] { "pending", "completed", "failed", "expired", "cancelled" })
            .HasPostgresEnum("operation_type", new[] { "deposit", "withdrawal", "float_load" })
            .HasPostgresEnum("otp_purpose", new[] { "wallet_linking", "receipt_lookup" })
            .HasPostgresEnum("payment_direction", new[] { "incoming", "outgoing" })
            .HasPostgresEnum("payment_rail", new[] { "open_payments", "bre_b_simulated" })
            .HasPostgresEnum("receipt_issue_channel", new[] { "screen", "pdf", "print", "whatsapp", "sms" })
            .HasPostgresEnum("receipt_requester", new[] { "agent", "user", "system" })
            .HasPostgresEnum("settlement_status", new[] { "pending", "paid", "voided" });

        modelBuilder.Entity<Admin>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("admins_pkey");

            entity.ToTable("admins", tb => tb.HasComment("Administradores de Relia. Acceden al panel y pagan las comisiones."));

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Password).HasColumnName("password");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("agents_pkey");

            entity.ToTable("agents", tb => tb.HasComment("Comercios que operan efectivo con Relia. Incluye su acceso y su wallet."));

            entity.HasIndex(e => e.Phone, "ux_agents_phone").IsUnique();

            entity.HasIndex(e => e.PaymentPointer, "ux_agents_wallet").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.ContactPerson).HasColumnName("contact_person");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Password).HasColumnName("password");
            entity.Property(e => e.PaymentPointer).HasColumnName("payment_pointer");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<AgentBalance>(entity =>
        {
            entity.HasKey(e => e.AgentId).HasName("agent_balances_pkey");

            entity.ToTable("agent_balances", tb => tb.HasComment("Totales de cada corresponsal: cupo, comisión y efectivo esperado. Una fila por corresponsal."));

            entity.Property(e => e.AgentId)
                .ValueGeneratedNever()
                .HasColumnName("agent_id");
            entity.Property(e => e.AccruedCommission).HasColumnName("accrued_commission");
            entity.Property(e => e.ExpectedCash).HasColumnName("expected_cash");
            entity.Property(e => e.FloatBalance).HasColumnName("float_balance");
            entity.Property(e => e.SettledCommissionTotal).HasColumnName("settled_commission_total");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Agent).WithOne(p => p.AgentBalance)
                .HasForeignKey<AgentBalance>(d => d.AgentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("agent_balances_agent_id_fkey");
        });

        modelBuilder.Entity<Fee>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("fees_pkey");

            entity.ToTable("fees", tb => tb.HasComment("Tarifa por operación, versionada. Cada operación copia sus valores."));

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.AgentCommission).HasColumnName("agent_commission");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.ReliaCommission).HasColumnName("relia_commission");
            entity.Property(e => e.TotalCommission).HasColumnName("total_commission");
            entity.Property(e => e.ValidFrom)
                .HasDefaultValueSql("now()")
                .HasColumnName("valid_from");
            entity.Property(e => e.ValidUntil).HasColumnName("valid_until");
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ledger_entries_pkey");

            entity.ToTable("ledger_entries", tb => tb.HasComment("Libro de solo inserción del cupo, la comisión y el efectivo: antes, variación y después."));

            entity.HasIndex(e => new { e.AgentId, e.Id }, "ix_ledger_agent").IsDescending(false, true);

            entity.HasIndex(e => e.OperationId, "ux_ledger_operation")
                .IsUnique()
                .HasFilter("(operation_id IS NOT NULL)");

            entity.HasIndex(e => e.PaymentId, "ux_ledger_payment").IsUnique();

            entity.HasIndex(e => e.SettlementId, "ux_ledger_settlement")
                .IsUnique()
                .HasFilter("(settlement_id IS NOT NULL)");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.CashAfter).HasColumnName("cash_after");
            entity.Property(e => e.CashBefore).HasColumnName("cash_before");
            entity.Property(e => e.CashChange).HasColumnName("cash_change");
            entity.Property(e => e.CommissionAfter).HasColumnName("commission_after");
            entity.Property(e => e.CommissionBefore).HasColumnName("commission_before");
            entity.Property(e => e.CommissionChange).HasColumnName("commission_change");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.FloatAfter).HasColumnName("float_after");
            entity.Property(e => e.FloatBefore).HasColumnName("float_before");
            entity.Property(e => e.FloatChange).HasColumnName("float_change");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.SettlementId).HasColumnName("settlement_id");

            entity.HasOne(d => d.Agent).WithMany(p => p.LedgerEntries)
                .HasForeignKey(d => d.AgentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ledger_entries_agent_id_fkey");

            entity.HasOne(d => d.Operation).WithOne(p => p.LedgerEntry)
                .HasForeignKey<LedgerEntry>(d => d.OperationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ledger_entries_operation_id_fkey");

            entity.HasOne(d => d.Payment).WithOne(p => p.LedgerEntry)
                .HasForeignKey<LedgerEntry>(d => d.PaymentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ledger_entries_payment_id_fkey");

            entity.HasOne(d => d.Settlement).WithOne(p => p.LedgerEntry)
                .HasForeignKey<LedgerEntry>(d => d.SettlementId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("ledger_entries_settlement_id_fkey");
        });

        modelBuilder.Entity<Operation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("operations_pkey");

            entity.ToTable("operations", tb => tb.HasComment("Depósitos, retiros y cargas de cupo. Copia la tarifa y los montos calculados. Sus datos no cambian después de creada."));

            entity.HasIndex(e => new { e.AgentId, e.CreatedAt }, "ix_operations_agent").IsDescending(false, true);

            entity.HasIndex(e => e.CreatedAt, "ix_operations_pending").HasFilter("(status = 'pending'::operation_status)");

            entity.HasIndex(e => new { e.AgentId, e.IdempotencyKey }, "ux_operations_idempotency").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AgentCommission).HasColumnName("agent_commission");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.ChargedAmount).HasColumnName("charged_amount");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DisplayNumber)
                .HasComputedColumnSql("('OP-'::text || lpad((number)::text, 6, '0'::text))", true)
                .HasColumnName("display_number");
            entity.Property(e => e.FeeId).HasColumnName("fee_id");
            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key");
            entity.Property(e => e.NetAmount).HasColumnName("net_amount");
            entity.Property(e => e.Number)
                .ValueGeneratedOnAdd()
                .UseIdentityAlwaysColumn()
                .HasColumnName("number");
            entity.Property(e => e.ReliaCommission).HasColumnName("relia_commission");
            entity.Property(e => e.RequestedAmount).HasColumnName("requested_amount");
            entity.Property(e => e.RoundValue).HasColumnName("round_value");
            entity.Property(e => e.TotalCommission).HasColumnName("total_commission");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WalletKeyId).HasColumnName("wallet_key_id");

            entity.HasOne(d => d.Agent).WithMany(p => p.Operations)
                .HasForeignKey(d => d.AgentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("operations_agent_id_fkey");

            entity.HasOne(d => d.Fee).WithMany(p => p.Operations)
                .HasForeignKey(d => d.FeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("operations_fee_id_fkey");

            entity.HasOne(d => d.WalletKey).WithMany(p => p.Operations)
                .HasForeignKey(d => d.WalletKeyId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("operations_wallet_key_id_fkey");
        });

        modelBuilder.Entity<OperationEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("operation_events_pkey");

            entity.ToTable("operation_events", tb => tb.HasComment("Bitácora de solo inserción con cada cambio de estado de una operación."));

            entity.HasIndex(e => new { e.OperationId, e.Id }, "ix_operation_events");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Details).HasColumnName("details");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");

            entity.HasOne(d => d.Operation).WithMany(p => p.OperationEvents)
                .HasForeignKey(d => d.OperationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("operation_events_operation_id_fkey");
        });

        modelBuilder.Entity<OtpChallenge>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("otp_challenges_pkey");

            entity.ToTable("otp_challenges", tb => tb.HasComment("OTP de vinculación de wallet y de consulta de recibos. Código guardado como hash, con expiración e intentos."));

            entity.HasIndex(e => new { e.Mobile, e.CreatedAt }, "ix_otp_mobile").IsDescending(false, true);

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Attempts).HasColumnName("attempts");
            entity.Property(e => e.CodeHash).HasColumnName("code_hash");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.MaxAttempts)
                .HasDefaultValue((short)5)
                .HasColumnName("max_attempts");
            entity.Property(e => e.Mobile).HasColumnName("mobile");
            entity.Property(e => e.PaymentPointer).HasColumnName("payment_pointer");
            entity.Property(e => e.VerifiedAt).HasColumnName("verified_at");
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("payments_pkey");

            entity.ToTable("payments", tb => tb.HasComment("Intentos de pago por riel (Open Payments o Bre-B simulado) de operaciones y liquidaciones."));

            entity.HasIndex(e => new { e.DestinationPaymentPointer, e.CreatedAt }, "ix_payments_destination").IsDescending(false, true);

            entity.HasIndex(e => e.ExpiresAt, "ix_payments_expiry").HasFilter("(status = 'pending'::operation_status)");

            entity.HasIndex(e => e.OperationId, "ix_payments_operation").HasFilter("(operation_id IS NOT NULL)");

            entity.HasIndex(e => e.SettlementId, "ix_payments_settlement").HasFilter("(settlement_id IS NOT NULL)");

            entity.HasIndex(e => new { e.SourcePaymentPointer, e.CreatedAt }, "ix_payments_source").IsDescending(false, true);

            entity.HasIndex(e => e.OpIncomingPaymentUrl, "ux_payments_incoming_url")
                .IsUnique()
                .HasFilter("(op_incoming_payment_url IS NOT NULL)");

            entity.HasIndex(e => e.OpOutgoingPaymentUrl, "ux_payments_outgoing_url")
                .IsUnique()
                .HasFilter("(op_outgoing_payment_url IS NOT NULL)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Amount).HasColumnName("amount");
            entity.Property(e => e.ApprovalUrl).HasColumnName("approval_url");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .HasDefaultValueSql("'COP'::bpchar")
                .IsFixedLength()
                .HasColumnName("currency");
            entity.Property(e => e.DebitAssetCode).HasColumnName("debit_asset_code");
            entity.Property(e => e.DebitAssetScale).HasColumnName("debit_asset_scale");
            entity.Property(e => e.DebitValue).HasColumnName("debit_value");
            entity.Property(e => e.DestinationPaymentPointer).HasColumnName("destination_payment_pointer");
            entity.Property(e => e.ErrorCode).HasColumnName("error_code");
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.GrantContinueTokenEncrypted).HasColumnName("grant_continue_token_encrypted");
            entity.Property(e => e.GrantContinueUri).HasColumnName("grant_continue_uri");
            entity.Property(e => e.OpIncomingPaymentUrl).HasColumnName("op_incoming_payment_url");
            entity.Property(e => e.OpOutgoingPaymentUrl).HasColumnName("op_outgoing_payment_url");
            entity.Property(e => e.OpQuoteUrl).HasColumnName("op_quote_url");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.RailDetails)
                .HasDefaultValueSql("'{}'::jsonb")
                .HasColumnType("jsonb")
                .HasColumnName("rail_details");
            entity.Property(e => e.RailReference).HasColumnName("rail_reference");
            entity.Property(e => e.ReceivedAssetCode).HasColumnName("received_asset_code");
            entity.Property(e => e.ReceivedAssetScale).HasColumnName("received_asset_scale");
            entity.Property(e => e.ReceivedValue).HasColumnName("received_value");
            entity.Property(e => e.SettlementId).HasColumnName("settlement_id");
            entity.Property(e => e.SourcePaymentPointer).HasColumnName("source_payment_pointer");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Operation).WithMany(p => p.Payments)
                .HasForeignKey(d => d.OperationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("payments_operation_id_fkey");

            entity.HasOne(d => d.Settlement).WithMany(p => p.Payments)
                .HasForeignKey(d => d.SettlementId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("payments_settlement_id_fkey");
        });

        modelBuilder.Entity<PaymentEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("payment_events_pkey");

            entity.ToTable("payment_events", tb => tb.HasComment("Bitácora de solo inserción de cada pago: llamadas al riel, respuestas y cambios de estado."));

            entity.HasIndex(e => new { e.PaymentId, e.Id }, "ix_payment_events");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Details).HasColumnName("details");
            entity.Property(e => e.HttpStatus).HasColumnName("http_status");
            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.RailResponse)
                .HasColumnType("jsonb")
                .HasColumnName("rail_response");

            entity.HasOne(d => d.Payment).WithMany(p => p.PaymentEvents)
                .HasForeignKey(d => d.PaymentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("payment_events_payment_id_fkey");
        });

        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("receipts_pkey");

            entity.ToTable("receipts", tb => tb.HasComment("Contenido congelado del recibo de cada depósito o retiro completado. Solo inserción."));

            entity.HasIndex(e => e.Code, "receipts_code_key").IsUnique();

            entity.HasIndex(e => e.OperationId, "receipts_operation_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasDefaultValueSql("upper(substr(replace((gen_random_uuid())::text, '-'::text, ''::text), 1, 12))")
                .HasColumnName("code");
            entity.Property(e => e.ContentHash).HasColumnName("content_hash");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Data)
                .HasColumnType("jsonb")
                .HasColumnName("data");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");

            entity.HasOne(d => d.Operation).WithOne(p => p.Receipt)
                .HasForeignKey<Receipt>(d => d.OperationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("receipts_operation_id_fkey");
        });

        modelBuilder.Entity<ReceiptIssuance>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("receipt_issuances_pkey");

            entity.ToTable("receipt_issuances", tb => tb.HasComment("Registro de cada vez que un recibo se muestra, descarga o envía. Desde la segunda es copia."));

            entity.HasIndex(e => new { e.ReceiptId, e.Id }, "ix_receipt_issuances");

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.IsCopy).HasColumnName("is_copy");
            entity.Property(e => e.ReceiptId).HasColumnName("receipt_id");
            entity.Property(e => e.UserMobile).HasColumnName("user_mobile");

            entity.HasOne(d => d.Agent).WithMany(p => p.ReceiptIssuances)
                .HasForeignKey(d => d.AgentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("receipt_issuances_agent_id_fkey");

            entity.HasOne(d => d.Receipt).WithMany(p => p.ReceiptIssuances)
                .HasForeignKey(d => d.ReceiptId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("receipt_issuances_receipt_id_fkey");
        });

        modelBuilder.Entity<Settlement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("settlements_pkey");

            entity.ToTable("settlements", tb => tb.HasComment("Pago manual mensual de la comisión total pendiente, a la wallet del corresponsal."));

            entity.HasIndex(e => new { e.AgentId, e.CreatedAt }, "ix_settlements_agent").IsDescending(false, true);

            entity.HasIndex(e => e.AgentId, "ux_settlements_one_pending")
                .IsUnique()
                .HasFilter("(status = 'pending'::settlement_status)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.Amount).HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByAdminId).HasColumnName("created_by_admin_id");
            entity.Property(e => e.DestinationPaymentPointer).HasColumnName("destination_payment_pointer");
            entity.Property(e => e.DisplayNumber)
                .HasComputedColumnSql("('LQ-'::text || lpad((number)::text, 6, '0'::text))", true)
                .HasColumnName("display_number");
            entity.Property(e => e.Number)
                .ValueGeneratedOnAdd()
                .UseIdentityAlwaysColumn()
                .HasColumnName("number");
            entity.Property(e => e.PaidAt).HasColumnName("paid_at");
            entity.Property(e => e.PeriodEnd).HasColumnName("period_end");
            entity.Property(e => e.PeriodStart).HasColumnName("period_start");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.VoidReason).HasColumnName("void_reason");

            entity.HasOne(d => d.Agent).WithOne(p => p.Settlement)
                .HasForeignKey<Settlement>(d => d.AgentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("settlements_agent_id_fkey");

            entity.HasOne(d => d.CreatedByAdmin).WithMany(p => p.Settlements)
                .HasForeignKey(d => d.CreatedByAdminId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("settlements_created_by_admin_id_fkey");
        });

        modelBuilder.Entity<SimulatedMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("simulated_messages_pkey");

            entity.ToTable("simulated_messages", tb => tb.HasComment("Bandeja simulada de SMS y WhatsApp. Solo existe en el demo."));

            entity.HasIndex(e => new { e.Recipient, e.CreatedAt }, "ix_messages_recipient").IsDescending(false, true);

            entity.Property(e => e.Id)
                .UseIdentityAlwaysColumn()
                .HasColumnName("id");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.Link).HasColumnName("link");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.Recipient).HasColumnName("recipient");

            entity.HasOne(d => d.Operation).WithMany(p => p.SimulatedMessages)
                .HasForeignKey(d => d.OperationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("simulated_messages_operation_id_fkey");
        });

        modelBuilder.Entity<VAdminAgentSummary>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_admin_agent_summary");

            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.CommissionPaidToday).HasColumnName("commission_paid_today");
            entity.Property(e => e.CommissionPaidTotal).HasColumnName("commission_paid_total");
            entity.Property(e => e.CommissionPayable).HasColumnName("commission_payable");
            entity.Property(e => e.ContactPerson).HasColumnName("contact_person");
            entity.Property(e => e.DepositsCount).HasColumnName("deposits_count");
            entity.Property(e => e.DepositsValue).HasColumnName("deposits_value");
            entity.Property(e => e.ExpectedCash).HasColumnName("expected_cash");
            entity.Property(e => e.FloatBalance).HasColumnName("float_balance");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OperationsCount).HasColumnName("operations_count");
            entity.Property(e => e.PaymentPointer).HasColumnName("payment_pointer");
            entity.Property(e => e.TotalMoved).HasColumnName("total_moved");
            entity.Property(e => e.WithdrawalsCount).HasColumnName("withdrawals_count");
            entity.Property(e => e.WithdrawalsValue).HasColumnName("withdrawals_value");
        });

        modelBuilder.Entity<VAgentBalanceCheck>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_agent_balance_check");

            entity.Property(e => e.AccruedCommission).HasColumnName("accrued_commission");
            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.CashAdjustments).HasColumnName("cash_adjustments");
            entity.Property(e => e.CashCalculated).HasColumnName("cash_calculated");
            entity.Property(e => e.CashMismatch).HasColumnName("cash_mismatch");
            entity.Property(e => e.CashPaidWithdrawals).HasColumnName("cash_paid_withdrawals");
            entity.Property(e => e.CashReceivedDeposits).HasColumnName("cash_received_deposits");
            entity.Property(e => e.CommissionEarned).HasColumnName("commission_earned");
            entity.Property(e => e.CommissionMismatch).HasColumnName("commission_mismatch");
            entity.Property(e => e.ExpectedCash).HasColumnName("expected_cash");
            entity.Property(e => e.FloatBalance).HasColumnName("float_balance");
            entity.Property(e => e.FloatCalculated).HasColumnName("float_calculated");
            entity.Property(e => e.FloatLoaded).HasColumnName("float_loaded");
            entity.Property(e => e.FloatMismatch).HasColumnName("float_mismatch");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SettledCommissionTotal).HasColumnName("settled_commission_total");
        });

        modelBuilder.Entity<VAgentHistory>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_agent_history");

            entity.Property(e => e.AgentId).HasColumnName("agent_id");
            entity.Property(e => e.Amount).HasColumnName("amount");
            entity.Property(e => e.CashAfter).HasColumnName("cash_after");
            entity.Property(e => e.CashBefore).HasColumnName("cash_before");
            entity.Property(e => e.CommissionAfter).HasColumnName("commission_after");
            entity.Property(e => e.CommissionBefore).HasColumnName("commission_before");
            entity.Property(e => e.FloatAfter).HasColumnName("float_after");
            entity.Property(e => e.FloatBefore).HasColumnName("float_before");
            entity.Property(e => e.LedgerEntryId).HasColumnName("ledger_entry_id");
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.Reference).HasColumnName("reference");
            entity.Property(e => e.SettlementId).HasColumnName("settlement_id");
            entity.Property(e => e.Type).HasColumnName("type");
        });

        modelBuilder.Entity<VWalletTraceability>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_wallet_traceability");

            entity.Property(e => e.Amount).HasColumnName("amount");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.Currency)
                .HasMaxLength(3)
                .IsFixedLength()
                .HasColumnName("currency");
            entity.Property(e => e.DestinationPaymentPointer).HasColumnName("destination_payment_pointer");
            entity.Property(e => e.OpIncomingPaymentUrl).HasColumnName("op_incoming_payment_url");
            entity.Property(e => e.OpOutgoingPaymentUrl).HasColumnName("op_outgoing_payment_url");
            entity.Property(e => e.OperationId).HasColumnName("operation_id");
            entity.Property(e => e.OperationNumber).HasColumnName("operation_number");
            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.PaymentPointer).HasColumnName("payment_pointer");
            entity.Property(e => e.SettlementId).HasColumnName("settlement_id");
            entity.Property(e => e.SettlementNumber).HasColumnName("settlement_number");
            entity.Property(e => e.Side).HasColumnName("side");
            entity.Property(e => e.SourcePaymentPointer).HasColumnName("source_payment_pointer");
        });

        modelBuilder.Entity<WalletKey>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("wallet_keys_pkey");

            entity.ToTable("wallet_keys", tb => tb.HasComment("Celular vinculado a un payment pointer. Inmutable. Borrar solo con TRUNCATE en el reinicio del demo."));

            entity.HasIndex(e => e.Mobile, "ux_wallet_keys_mobile").IsUnique();

            entity.HasIndex(e => e.WalletAddressUrl, "ux_wallet_keys_wallet").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.LinkedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("linked_at");
            entity.Property(e => e.Mobile).HasColumnName("mobile");
            entity.Property(e => e.PaymentPointer).HasColumnName("payment_pointer");
            entity.Property(e => e.WalletAddressUrl)
                .HasComputedColumnSql("('https://'::text || substr(payment_pointer, 2))", true)
                .HasColumnName("wallet_address_url");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
