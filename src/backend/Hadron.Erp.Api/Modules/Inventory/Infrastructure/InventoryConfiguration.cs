using Hadron.Erp.Api.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Inventory.Infrastructure;

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses", "inventory", table =>
        {
            table.HasCheckConstraint("ck_warehouses_business_number_format",
                "\"BusinessNumber\" ~ '^WH-[0-9]{4,}$'");
            table.HasCheckConstraint("ck_warehouses_version", "\"Version\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NameFa).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.BusinessNumber).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.BusinessNumber });
    }
}

internal sealed class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("transactions", "inventory", table =>
        {
            table.HasCheckConstraint("ck_inventory_transaction_type",
                "\"TransactionType\" IN ('OPENING','RECEIPT','ISSUE','ADJ_IN','ADJ_OUT','TRANSFER','REVERSAL')");
            table.HasCheckConstraint("ck_inventory_quantity", "\"Quantity\" > 0");
            table.HasCheckConstraint("ck_inventory_transfer_shape",
                "(\"TransactionType\" = 'TRANSFER' AND \"DestinationWarehouseId\" IS NOT NULL AND \"DestinationWarehouseId\" <> \"WarehouseId\") OR " +
                "(\"TransactionType\" <> 'TRANSFER' AND \"TransactionType\" <> 'REVERSAL' AND \"DestinationWarehouseId\" IS NULL) OR " +
                "(\"TransactionType\" = 'REVERSAL')");
            table.HasCheckConstraint("ck_inventory_reason",
                "\"TransactionType\" NOT IN ('ADJ_IN','ADJ_OUT','REVERSAL') OR NULLIF(BTRIM(\"Reason\"), '') IS NOT NULL");
            table.HasCheckConstraint("ck_inventory_original_shape",
                "(\"TransactionType\" = 'REVERSAL') = (\"OriginalTransactionId\" IS NOT NULL)");
            table.HasCheckConstraint("ck_inventory_source_pair",
                "(\"SourceDocumentType\" IS NULL) = (\"SourceDocumentId\" IS NULL)");
            table.HasCheckConstraint("ck_inventory_source_type",
                "\"SourceDocumentType\" IS NULL OR \"SourceDocumentType\" IN ('PURCHASE_RECEIPT','PRODUCTION_ISSUE','LEGACY_IMPORT')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TransactionType).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.SourceDocumentType).HasMaxLength(40);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginalTransaction).WithMany().HasForeignKey(x => x.OriginalTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ItemId, x.WarehouseId, x.EffectiveDate });
        builder.HasIndex(x => new { x.ItemId, x.DestinationWarehouseId, x.EffectiveDate });
        builder.HasIndex(x => new { x.EffectiveDate, x.TransactionType });
        builder.HasIndex(x => x.SourceDocumentId);
        builder.HasIndex(x => x.OriginalTransactionId).IsUnique()
            .HasFilter("\"OriginalTransactionId\" IS NOT NULL");
        builder.HasIndex(x => new { x.ItemId, x.WarehouseId }).IsUnique()
            .HasFilter("\"TransactionType\" = 'OPENING'");
    }
}

internal sealed class InventoryLedgerEffectConfiguration : IEntityTypeConfiguration<InventoryLedgerEffect>
{
    public void Configure(EntityTypeBuilder<InventoryLedgerEffect> builder)
    {
        builder.HasNoKey();
        builder.ToView("ledger_effects", "inventory");
        builder.Property(x => x.QuantityEffect).HasPrecision(18, 6);
    }
}
