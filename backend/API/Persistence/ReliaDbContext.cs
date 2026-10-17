using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Relia.Api.Domain;

namespace Relia.Api.Persistence;

/// <summary>
/// Mapeo de EF Core al esquema backend/db/db_ddl.sql. El esquema lo crea ese script, no EF: no hay
/// migraciones. Triggers, CHECK y enums viven en la base.
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
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Registra los enums de PostgreSQL. Se llama desde UseNpgsql.</summary>
    public static void MapEnums(NpgsqlDbContextOptionsBuilder o)
    {
        o.MapEnum<OperationKind>("operation_type");
        o.MapEnum<TopUpMethodKind>("load_method");
        o.MapEnum<OperationState>("operation_status");
        o.MapEnum<SettlementState>("settlement_status");
        o.MapEnum<PaymentRail>("payment_rail");
        o.MapEnum<PaymentDirection>("payment_direction");
        o.MapEnum<MessageChannel>("message_channel");
        o.MapEnum<MessageKind>("message_type");
        o.MapEnum<LedgerEntryKind>("ledger_entry_type");
        o.MapEnum<OtpPurpose>("otp_purpose");
        o.MapEnum<ReceiptChannel>("receipt_issue_channel");
        o.MapEnum<ReceiptRequester>("receipt_requester");
        o.MapEnum<EventOrigin>("event_source");
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Fee>(e =>
        {
            e.ToTable("fees");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Total).HasColumnName("total_commission");
            e.Property(x => x.ReliaShare).HasColumnName("relia_commission");
            e.Property(x => x.AgentShare).HasColumnName("agent_commission");
            e.Property(x => x.ValidFrom).HasColumnName("valid_from").HasDefaultValueSql("now()");
            e.Property(x => x.ValidTo).HasColumnName("valid_until");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<Agent>(e =>
        {
            e.ToTable("agents");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.ManagerName).HasColumnName("contact_person");
            e.Property(x => x.City).HasColumnName("city");
            e.Property(x => x.Address).HasColumnName("address");
            e.Property(x => x.Phone).HasColumnName("phone");
            e.Property(x => x.WalletAddress).HasColumnName("payment_pointer");
            e.Property(x => x.Password).HasColumnName("password");
            e.Property(x => x.Active).HasColumnName("active");
            e.Property(x => x.WalletSimulated).HasColumnName("wallet_simulated");
            CreatedAt(e.Property(x => x.CreatedAt));
            UpdatedAt(e.Property(x => x.UpdatedAt));
            e.HasOne(x => x.Balance).WithOne().HasForeignKey<AgentBalance>(x => x.AgentId);
        });

        b.Entity<Admin>(e =>
        {
            e.ToTable("admins");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Email).HasColumnName("email");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Password).HasColumnName("password");
            e.Property(x => x.Active).HasColumnName("active");
            CreatedAt(e.Property(x => x.CreatedAt));
            UpdatedAt(e.Property(x => x.UpdatedAt));
        });

        b.Entity<AgentBalance>(e =>
        {
            e.ToTable("agent_balances");
            e.HasKey(x => x.AgentId);
            e.Property(x => x.AgentId).HasColumnName("agent_id");
            e.Property(x => x.Quota).HasColumnName("float_balance");
            e.Property(x => x.AccruedCommission).HasColumnName("accrued_commission");
            e.Property(x => x.SettledCommissionTotal).HasColumnName("settled_commission_total");
            e.Property(x => x.CashOnHand).HasColumnName("expected_cash");
            UpdatedAt(e.Property(x => x.UpdatedAt));
        });

        b.Entity<WalletKey>(e =>
        {
            e.ToTable("wallet_keys");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Phone).HasColumnName("mobile");
            e.Property(x => x.WalletAddress).HasColumnName("payment_pointer");
            e.Property(x => x.WalletAddressUrl).HasColumnName("wallet_address_url")
                .HasComputedColumnSql("'https://' || substr(payment_pointer, 2)", stored: true);
            e.Property(x => x.LinkedAt).HasColumnName("linked_at").HasDefaultValueSql("now()");
        });

        b.Entity<OtpChallenge>(e =>
        {
            e.ToTable("otp_challenges");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Phone).HasColumnName("mobile");
            e.Property(x => x.Purpose).HasColumnName("purpose");
            e.Property(x => x.WalletAddress).HasColumnName("payment_pointer");
            e.Property(x => x.CodeHash).HasColumnName("code_hash");
            e.Property(x => x.Attempts).HasColumnName("attempts");
            e.Property(x => x.MaxAttempts).HasColumnName("max_attempts");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.VerifiedAt).HasColumnName("verified_at");
            CreatedAt(e.Property(x => x.CreatedAt));
        });

        b.Entity<Operation>(e =>
        {
            e.ToTable("operations");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Number).HasColumnName("number").UseIdentityAlwaysColumn();
            e.Property(x => x.Reference).HasColumnName("display_number")
                .HasComputedColumnSql("'OP-' || lpad(number::text, 6, '0')", stored: true);
            e.Property(x => x.AgentId).HasColumnName("agent_id");
            e.Property(x => x.Kind).HasColumnName("type");
            e.Property(x => x.TopUpMethod).HasColumnName("load_method");
            e.Property(x => x.WalletKeyId).HasColumnName("wallet_key_id");
            e.Property(x => x.RequestedAmount).HasColumnName("requested_amount");
            e.Property(x => x.RoundAmount).HasColumnName("round_value");
            e.Property(x => x.FeeId).HasColumnName("fee_id");
            e.Property(x => x.FeeTotal).HasColumnName("total_commission");
            e.Property(x => x.FeeRelia).HasColumnName("relia_commission");
            e.Property(x => x.FeeAgent).HasColumnName("agent_commission");
            e.Property(x => x.NetAmount).HasColumnName("net_amount");
            e.Property(x => x.ChargedAmount).HasColumnName("charged_amount");
            e.Property(x => x.State).HasColumnName("status");
            e.Property(x => x.FailureCode).HasColumnName("failure_code");
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
            CreatedAt(e.Property(x => x.CreatedAt));
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.CashHandedOverAt).HasColumnName("cash_handed_over_at");
            UpdatedAt(e.Property(x => x.UpdatedAt));
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId);
            e.HasOne(x => x.WalletKey).WithMany().HasForeignKey(x => x.WalletKeyId);
        });

        b.Entity<SimulatedMessage>(e =>
        {
            e.ToTable("simulated_messages");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Channel).HasColumnName("channel");
            e.Property(x => x.Kind).HasColumnName("type");
            e.Property(x => x.Recipient).HasColumnName("recipient");
            e.Property(x => x.Content).HasColumnName("content");
            e.Property(x => x.Link).HasColumnName("link");
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.Read).HasColumnName("is_read");
            CreatedAt(e.Property(x => x.CreatedAt));
            e.HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId);
        });

        b.Entity<Settlement>(e =>
        {
            e.ToTable("settlements");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.Number).HasColumnName("number").UseIdentityAlwaysColumn();
            e.Property(x => x.Reference).HasColumnName("display_number")
                .HasComputedColumnSql("'LQ-' || lpad(number::text, 6, '0')", stored: true);
            e.Property(x => x.AgentId).HasColumnName("agent_id");
            e.Property(x => x.CreatedByAdminId).HasColumnName("created_by_admin_id");
            e.Property(x => x.DestinationWalletAddress).HasColumnName("destination_payment_pointer");
            e.Property(x => x.PeriodFrom).HasColumnName("period_start");
            e.Property(x => x.PeriodTo).HasColumnName("period_end");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.State).HasColumnName("status");
            e.Property(x => x.PaidAt).HasColumnName("paid_at");
            e.Property(x => x.VoidReason).HasColumnName("void_reason");
            CreatedAt(e.Property(x => x.CreatedAt));
            UpdatedAt(e.Property(x => x.UpdatedAt));
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId);
            e.HasOne(x => x.CreatedByAdmin).WithMany().HasForeignKey(x => x.CreatedByAdminId);
        });

        b.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.SettlementId).HasColumnName("settlement_id");
            e.Property(x => x.Rail).HasColumnName("rail");
            e.Property(x => x.Direction).HasColumnName("direction");
            e.Property(x => x.State).HasColumnName("status");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.Currency).HasColumnName("currency").HasColumnType("char(3)");
            e.Property(x => x.SourceWalletAddress).HasColumnName("source_payment_pointer");
            e.Property(x => x.DestinationWalletAddress).HasColumnName("destination_payment_pointer");
            e.Property(x => x.IncomingPaymentUrl).HasColumnName("op_incoming_payment_url");
            e.Property(x => x.QuoteUrl).HasColumnName("op_quote_url");
            e.Property(x => x.OutgoingPaymentUrl).HasColumnName("op_outgoing_payment_url");
            e.Property(x => x.ApprovalUrl).HasColumnName("approval_url");
            e.Property(x => x.GrantContinueUri).HasColumnName("grant_continue_uri");
            e.Property(x => x.GrantContinueTokenEncrypted).HasColumnName("grant_continue_token_encrypted");
            e.Property(x => x.RailReference).HasColumnName("rail_reference");
            e.Property(x => x.RailPaymentId).HasColumnName("rail_payment_id");
            e.Property(x => x.RailVersion).HasColumnName("rail_version");
            e.Property(x => x.DebitValue).HasColumnName("debit_value");
            e.Property(x => x.DebitAssetCode).HasColumnName("debit_asset_code");
            e.Property(x => x.DebitAssetScale).HasColumnName("debit_asset_scale");
            e.Property(x => x.ReceivedValue).HasColumnName("received_value");
            e.Property(x => x.ReceivedAssetCode).HasColumnName("received_asset_code");
            e.Property(x => x.ReceivedAssetScale).HasColumnName("received_asset_scale");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.Property(x => x.ErrorCode).HasColumnName("error_code");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.RailDetail).HasColumnName("rail_details").HasColumnType("jsonb");
            CreatedAt(e.Property(x => x.CreatedAt));
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            UpdatedAt(e.Property(x => x.UpdatedAt));
            e.HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId);
            e.HasOne<Settlement>().WithMany().HasForeignKey(x => x.SettlementId);
        });

        b.Entity<PaymentEvent>(e =>
        {
            e.ToTable("payment_events");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.PaymentId).HasColumnName("payment_id");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.PreviousState).HasColumnName("previous_status");
            e.Property(x => x.NewState).HasColumnName("new_status");
            e.Property(x => x.Origin).HasColumnName("source");
            e.Property(x => x.HttpStatus).HasColumnName("http_status");
            e.Property(x => x.RailResponse).HasColumnName("rail_response").HasColumnType("jsonb");
            e.Property(x => x.Detail).HasColumnName("details");
            CreatedAt(e.Property(x => x.CreatedAt));
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
        });

        b.Entity<OperationEvent>(e =>
        {
            e.ToTable("operation_events");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.PreviousState).HasColumnName("previous_status");
            e.Property(x => x.NewState).HasColumnName("new_status");
            e.Property(x => x.Origin).HasColumnName("source");
            e.Property(x => x.Detail).HasColumnName("details");
            CreatedAt(e.Property(x => x.CreatedAt));
            e.HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId);
        });

        b.Entity<LedgerEntry>(e =>
        {
            e.ToTable("ledger_entries");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.AgentId).HasColumnName("agent_id");
            e.Property(x => x.Kind).HasColumnName("type");
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.SettlementId).HasColumnName("settlement_id");
            e.Property(x => x.PaymentId).HasColumnName("payment_id");
            e.Property(x => x.QuotaBefore).HasColumnName("float_before");
            e.Property(x => x.QuotaChange).HasColumnName("float_change");
            e.Property(x => x.QuotaAfter).HasColumnName("float_after");
            e.Property(x => x.CommissionBefore).HasColumnName("commission_before");
            e.Property(x => x.CommissionChange).HasColumnName("commission_change");
            e.Property(x => x.CommissionAfter).HasColumnName("commission_after");
            e.Property(x => x.CashBefore).HasColumnName("cash_before");
            e.Property(x => x.CashChange).HasColumnName("cash_change");
            e.Property(x => x.CashAfter).HasColumnName("cash_after");
            e.Property(x => x.Reason).HasColumnName("reason");
            CreatedAt(e.Property(x => x.CreatedAt));
            // Sin estas relaciones EF no ordena los INSERT y la base rechaza las llaves foráneas.
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId);
            e.HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId);
            e.HasOne<Settlement>().WithMany().HasForeignKey(x => x.SettlementId);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
        });

        b.Entity<Receipt>(e =>
        {
            e.ToTable("receipts");
            UuidKey(e.Property(x => x.Id));
            e.Property(x => x.OperationId).HasColumnName("operation_id");
            e.Property(x => x.Code).HasColumnName("code")
                .HasDefaultValueSql("upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 12))");
            e.Property(x => x.Data).HasColumnName("data").HasColumnType("jsonb");
            e.Property(x => x.ContentHash).HasColumnName("content_hash");
            CreatedAt(e.Property(x => x.CreatedAt));
            e.HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId);
        });

        b.Entity<ReceiptIssue>(e =>
        {
            e.ToTable("receipt_issuances");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.ReceiptId).HasColumnName("receipt_id");
            e.Property(x => x.Channel).HasColumnName("channel");
            e.Property(x => x.RequestedBy).HasColumnName("requested_by");
            e.Property(x => x.AgentId).HasColumnName("agent_id");
            e.Property(x => x.CustomerPhone).HasColumnName("user_mobile");
            // Lo calcula el trigger mark_receipt_copy al insertar.
            e.Property(x => x.IsCopy).HasColumnName("is_copy").ValueGeneratedOnAdd();
            CreatedAt(e.Property(x => x.CreatedAt));
            e.HasOne<Receipt>().WithMany().HasForeignKey(x => x.ReceiptId);
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records");
            e.Property(x => x.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            e.Property(x => x.Scope).HasColumnName("scope");
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
            e.Property(x => x.RequestHash).HasColumnName("request_hash");
            e.Property(x => x.ResponseStatus).HasColumnName("response_status");
            e.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("jsonb");
            e.Property(x => x.ResourceId).HasColumnName("resource_id");
            CreatedAt(e.Property(x => x.CreatedAt));
        });
    }

    private static void UuidKey(PropertyBuilder<Guid> p) =>
        p.HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

    private static void CreatedAt(PropertyBuilder<DateTimeOffset> p) =>
        p.HasColumnName("created_at").HasDefaultValueSql("now()");

    // Lo mantiene el trigger set_updated_at.
    private static void UpdatedAt(PropertyBuilder<DateTimeOffset> p) =>
        p.HasColumnName("updated_at").HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
}
