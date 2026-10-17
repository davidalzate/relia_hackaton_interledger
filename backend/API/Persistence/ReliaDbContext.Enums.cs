using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Relia.Api.Entities;

namespace Relia.Api.Persistence;

// Complemento manual del contexto generado por scaffold: mapea las columnas enum que el scaffold omite.
// Los nombres de columna se dedujeron del esquema v0.2 traducido; si alguno no coincide con la base,
// falla la primera consulta a esa tabla.
public partial class ReliaDbContext
{
    /// <summary>Registra los enums de PostgreSQL. Se pasa a UseNpgsql en Program.cs.</summary>
    public static void MapEnums(NpgsqlDbContextOptionsBuilder o)
    {
        o.MapEnum<OperationType>("operation_type");
        o.MapEnum<LoadMethod>("load_method");
        o.MapEnum<OperationStatus>("operation_status");
        o.MapEnum<SettlementStatus>("settlement_status");
        o.MapEnum<PaymentRail>("payment_rail");
        o.MapEnum<PaymentDirection>("payment_direction");
        o.MapEnum<MessageChannel>("message_channel");
        o.MapEnum<MessageType>("message_type");
        o.MapEnum<LedgerEntryType>("ledger_entry_type");
        o.MapEnum<OtpPurpose>("otp_purpose");
        o.MapEnum<ReceiptIssueChannel>("receipt_issue_channel");
        o.MapEnum<ReceiptRequester>("receipt_requester");
        o.MapEnum<EventSource>("event_source");
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // El scaffold leyó el índice único parcial "una liquidación pendiente por corresponsal" como una
        // relación uno a uno. Es uno a muchos: sin esto, EF desliga la liquidación anterior al crear otra.
        modelBuilder.Entity<Agent>().Ignore(a => a.Settlement);
        modelBuilder.Entity<Settlement>().HasOne(s => s.Agent).WithMany()
            .HasForeignKey(s => s.AgentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("settlements_agent_id_fkey");

        modelBuilder.Entity<Operation>(e =>
        {
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.LoadMethod).HasColumnName("load_method");
            e.Property(x => x.Status).HasColumnName("status");
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.Property(x => x.Rail).HasColumnName("rail");
            e.Property(x => x.Direction).HasColumnName("direction");
            e.Property(x => x.Status).HasColumnName("status");
        });

        modelBuilder.Entity<Settlement>().Property(x => x.Status).HasColumnName("status");

        modelBuilder.Entity<SimulatedMessage>(e =>
        {
            e.Property(x => x.Channel).HasColumnName("channel");
            e.Property(x => x.Type).HasColumnName("type");
        });

        modelBuilder.Entity<LedgerEntry>().Property(x => x.Type).HasColumnName("type");

        modelBuilder.Entity<OtpChallenge>().Property(x => x.Purpose).HasColumnName("purpose");

        modelBuilder.Entity<ReceiptIssuance>(e =>
        {
            e.Property(x => x.Channel).HasColumnName("channel");
            e.Property(x => x.RequestedBy).HasColumnName("requested_by");
        });

        modelBuilder.Entity<OperationEvent>(e =>
        {
            e.Property(x => x.PreviousStatus).HasColumnName("previous_status");
            e.Property(x => x.NewStatus).HasColumnName("new_status");
            e.Property(x => x.Source).HasColumnName("source");
        });

        modelBuilder.Entity<PaymentEvent>(e =>
        {
            e.Property(x => x.PreviousStatus).HasColumnName("previous_status");
            e.Property(x => x.NewStatus).HasColumnName("new_status");
            e.Property(x => x.Source).HasColumnName("source");
        });
    }
}
