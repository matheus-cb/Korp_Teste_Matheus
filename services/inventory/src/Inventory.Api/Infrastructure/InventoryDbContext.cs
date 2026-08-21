using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Api.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductAuditEvent> ProductAuditEvents => Set<ProductAuditEvent>();
    public DbSet<StockDebitOperation> StockDebitOperations => Set<StockDebitOperation>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new ProductAuditEventConfiguration());
        modelBuilder.ApplyConfiguration(new StockDebitOperationConfiguration());
        modelBuilder.ApplyConfiguration(new StockMovementConfiguration());
    }

    private sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.ToTable("Products", table =>
                table.HasCheckConstraint("CK_Products_Balance", "\"Balance\" >= 0"));
            builder.HasKey(product => product.Id);
            builder.Property(product => product.Code).HasMaxLength(64).IsRequired();
            builder.Property(product => product.Description).HasMaxLength(200).IsRequired();
            builder.Property(product => product.Balance).IsRequired();
            builder.Property(product => product.TracksStock)
                .IsRequired()
                .HasDefaultValue(true);
            builder.Property(product => product.CreatedAt).IsRequired();
            builder.Property(product => product.CreatedBy).HasMaxLength(120).IsRequired().HasDefaultValue("sistema");
            builder.Property(product => product.UpdatedAt).IsRequired();
            builder.Property(product => product.UpdatedBy).HasMaxLength(120).IsRequired().HasDefaultValue("sistema");
            builder.Property(product => product.Version).IsConcurrencyToken();
            builder.HasMany(product => product.AuditEvents)
                .WithOne()
                .HasForeignKey(auditEvent => auditEvent.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(product => product.Code)
                .IsUnique()
                .HasDatabaseName("UX_Products_Code");
        }
    }

    private sealed class ProductAuditEventConfiguration : IEntityTypeConfiguration<ProductAuditEvent>
    {
        public void Configure(EntityTypeBuilder<ProductAuditEvent> builder)
        {
            builder.ToTable("ProductAuditEvents");
            builder.HasKey(auditEvent => auditEvent.Id);
            builder.Property(auditEvent => auditEvent.Type).HasMaxLength(24).IsRequired();
            builder.Property(auditEvent => auditEvent.ActorName).HasMaxLength(120).IsRequired();
            builder.Property(auditEvent => auditEvent.OccurredAt).IsRequired();
            builder.HasIndex(auditEvent => new { auditEvent.ProductId, auditEvent.OccurredAt });
        }
    }

    private sealed class StockDebitOperationConfiguration : IEntityTypeConfiguration<StockDebitOperation>
    {
        public void Configure(EntityTypeBuilder<StockDebitOperation> builder)
        {
            builder.ToTable("StockDebitOperations");
            builder.HasKey(operation => operation.Id);
            builder.Property(operation => operation.AttemptId).IsRequired();
            builder.Property(operation => operation.InvoiceId).IsRequired();
            builder.Property(operation => operation.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
            builder.Property(operation => operation.State)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();
            builder.Property(operation => operation.ErrorCode).HasMaxLength(64);
            builder.Property(operation => operation.ErrorMessage).HasMaxLength(500);
            builder.Property(operation => operation.IgnoredItemsJson).HasColumnType("jsonb");
            builder.Property(operation => operation.CreatedAt).IsRequired();
            builder.HasIndex(operation => operation.AttemptId)
                .IsUnique()
                .HasDatabaseName("UX_StockDebitOperations_AttemptId");
        }
    }

    private sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
    {
        public void Configure(EntityTypeBuilder<StockMovement> builder)
        {
            builder.ToTable("StockMovements", table =>
            {
                table.HasCheckConstraint("CK_StockMovements_Quantity", "\"Quantity\" > 0");
                table.HasCheckConstraint(
                    "CK_StockMovements_Balances",
                    "\"BalanceBefore\" >= 0 AND \"BalanceAfter\" >= 0 AND \"BalanceAfter\" = \"BalanceBefore\" - \"Quantity\"");
            });
            builder.HasKey(movement => movement.Id);
            builder.Property(movement => movement.Quantity).IsRequired();
            builder.Property(movement => movement.BalanceBefore).IsRequired();
            builder.Property(movement => movement.BalanceAfter).IsRequired();
            builder.Property(movement => movement.CreatedAt).IsRequired();
            builder.HasOne(movement => movement.Operation)
                .WithMany(operation => operation.Movements)
                .HasForeignKey(movement => movement.OperationId)
                .OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(movement => movement.Product)
                .WithMany()
                .HasForeignKey(movement => movement.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(movement => movement.OperationId);
            builder.HasIndex(movement => movement.ProductId);
        }
    }
}
