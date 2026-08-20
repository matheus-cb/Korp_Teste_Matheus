using Billing.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Infrastructure;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<InvoiceClosureAttempt> ClosureAttempts => Set<InvoiceClosureAttempt>();
    public DbSet<AiDraftRun> AiDraftRuns => Set<AiDraftRun>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<InvoiceImport> Imports => Set<InvoiceImport>();
    public DbSet<InvoiceAuditEvent> InvoiceAuditEvents => Set<InvoiceAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("invoice_number_seq").StartsAt(1).IncrementsBy(1);

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasDefaultValueSql("nextval('invoice_number_seq')");
            entity.HasIndex(x => x.Number).IsUnique();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.CreatedBy).HasMaxLength(120).IsRequired().HasDefaultValue("sistema");
            entity.Property(x => x.ClosedBy).HasMaxLength(120);
            entity.Property(x => x.UpdatedBy).HasMaxLength(120).IsRequired().HasDefaultValue("sistema");
            entity.Property(x => x.UpdatedAt).IsRequired();
            entity.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.ClosureAttempts).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.AuditEvents).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceAuditEvent>(entity =>
        {
            entity.ToTable("invoice_audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasMaxLength(24).IsRequired();
            entity.Property(x => x.ActorName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.OccurredAt).IsRequired();
            entity.HasIndex(x => new { x.InvoiceId, x.OccurredAt });
        });

        modelBuilder.Entity<InvoiceImport>(entity =>
        {
            entity.ToTable("invoice_imports");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            entity.Property(x => x.ContentHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(x => x.CreatedBy).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.ContentHash).IsUnique().HasDatabaseName("ux_invoice_imports_hash");
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserName).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
            entity.HasIndex(x => x.UserName).IsUnique().HasDatabaseName("ux_users_username");
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_user_sessions_token");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceItem>(entity =>
        {
            entity.ToTable("invoice_items");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductCode).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ProductDescription).HasMaxLength(300).IsRequired();
            entity.HasIndex(x => new { x.InvoiceId, x.ProductId }).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint("ck_invoice_items_quantity", "\"Quantity\" > 0"));
        });

        modelBuilder.Entity<InvoiceClosureAttempt>(entity =>
        {
            entity.ToTable("invoice_closure_attempts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.ErrorCode).HasMaxLength(64);
            entity.Property(x => x.ErrorMessage).HasMaxLength(500);
            entity.Property(x => x.IgnoredItemsJson).HasColumnType("jsonb");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.InvoiceId)
                .IsUnique()
                .HasFilter("\"State\" = 'Pending'");
            entity.HasIndex(x => new { x.State, x.NextRetryAt });
        });

        modelBuilder.Entity<AiDraftRun>(entity =>
        {
            entity.ToTable("ai_draft_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Model).HasMaxLength(100);
            entity.Property(x => x.PromptVersion).HasMaxLength(100);
            entity.Property(x => x.ToolNames).HasColumnType("jsonb");
            entity.Property(x => x.EstimatedCostUsd).HasPrecision(18, 8);
            entity.Property(x => x.FailureCode).HasMaxLength(64);
        });
    }
}
