using Hadron.Erp.Api.Modules.Inventory.Domain;
using Hadron.Erp.Api.Modules.Identity.Domain;
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
            table.HasCheckConstraint("ck_warehouses_type",
                "\"WarehouseType\" IN ('STORAGE','PRODUCTION_STAGING','FINISHED_GOODS')");
            table.HasCheckConstraint("ck_warehouses_mrp_availability",
                "\"IncludeInMrpAvailable\" = (\"WarehouseType\" <> 'PRODUCTION_STAGING')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NameFa).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.AddressFa).HasMaxLength(1000);
        builder.Property(x => x.AddressEn).HasMaxLength(1000);
        builder.Property(x => x.WarehouseType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.BusinessNumber).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.BusinessNumber });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PrimaryResponsibleUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryItemUnitConversionConfiguration
    : IEntityTypeConfiguration<InventoryItemUnitConversion>
{
    public void Configure(EntityTypeBuilder<InventoryItemUnitConversion> builder)
    {
        builder.ToTable("item_unit_conversions", "inventory", table =>
        {
            table.HasCheckConstraint("ck_inventory_item_unit_factor", "\"FactorToBase\" > 0");
            table.HasCheckConstraint("ck_inventory_item_unit_different",
                "\"TransactionUnitId\" <> \"BaseUnitId\"");
            table.HasCheckConstraint("ck_inventory_item_unit_version", "\"Version\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FactorToBase).HasPrecision(28, 12);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.ItemId, x.TransactionUnitId }).IsUnique();
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TransactionUnit).WithMany().HasForeignKey(x => x.TransactionUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BaseUnit).WithMany().HasForeignKey(x => x.BaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
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
            table.HasCheckConstraint("ck_inventory_entered_quantity", "\"EnteredQuantity\" > 0");
            table.HasCheckConstraint("ck_inventory_conversion_factor", "\"ConversionFactorToBase\" > 0");
            table.HasCheckConstraint("ck_inventory_conversion_math",
                "ROUND(\"EnteredQuantity\" * \"ConversionFactorToBase\", 6) = \"Quantity\"");
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
                "\"SourceDocumentType\" IS NULL OR \"SourceDocumentType\" IN ('PURCHASE_RECEIPT','PRODUCTION_ISSUE','PRODUCTION_RECEIPT','INTERNAL_TRANSFER_DISPATCH','INTERNAL_TRANSFER_RECEIPT','LEGACY_IMPORT')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TransactionType).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.EnteredQuantity).HasPrecision(18, 6);
        builder.Property(x => x.ConversionFactorToBase).HasPrecision(28, 12);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.SourceDocumentType).HasMaxLength(40);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TransactionUnit).WithMany().HasForeignKey(x => x.TransactionUnitId)
            .OnDelete(DeleteBehavior.Restrict);
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

internal sealed class InventoryTransferConfiguration : IEntityTypeConfiguration<InventoryTransfer>
{
    public void Configure(EntityTypeBuilder<InventoryTransfer> builder)
    {
        builder.ToTable("transfers", "inventory", table =>
        {
            table.HasCheckConstraint("ck_inventory_transfer_number",
                "\"BusinessNumber\" ~ '^ITR-[0-9]{6,}$'");
            table.HasCheckConstraint("ck_inventory_transfer_status",
                "\"Status\" IN ('DRAFT','DISPATCHED','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')");
            table.HasCheckConstraint("ck_inventory_transfer_quantity", "\"RequestedQuantity\" > 0");
            table.HasCheckConstraint("ck_inventory_transfer_entered_quantity", "\"EnteredRequestedQuantity\" > 0");
            table.HasCheckConstraint("ck_inventory_transfer_conversion_factor", "\"ConversionFactorToBase\" > 0");
            table.HasCheckConstraint("ck_inventory_transfer_conversion_math",
                "ROUND(\"EnteredRequestedQuantity\" * \"ConversionFactorToBase\", 6) = \"RequestedQuantity\"");
            table.HasCheckConstraint("ck_inventory_transfer_warehouses",
                "\"SourceWarehouseId\" <> \"DestinationWarehouseId\"");
            table.HasCheckConstraint("ck_inventory_transfer_version", "\"Version\" >= 1");
            table.HasCheckConstraint("ck_inventory_transfer_cancel",
                "(\"CancelledAtUtc\" IS NULL) = (\"CancellationReason\" IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.EnteredRequestedQuantity).HasPrecision(18, 6);
        builder.Property(x => x.ConversionFactorToBase).HasPrecision(28, 12);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.CancellationReason).HasMaxLength(1000);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.BusinessNumber).IsUnique();
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.ItemId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.SourceWarehouseId, x.DestinationWarehouseId, x.CreatedAtUtc });
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TransactionUnit).WithMany().HasForeignKey(x => x.TransactionUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceWarehouse).WithMany().HasForeignKey(x => x.SourceWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DispatchTransaction).WithMany().HasForeignKey(x => x.DispatchTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InventoryTransferReceiptConfiguration
    : IEntityTypeConfiguration<InventoryTransferReceipt>
{
    public void Configure(EntityTypeBuilder<InventoryTransferReceipt> builder)
    {
        builder.ToTable("transfer_receipts", "inventory", table =>
        {
            table.HasCheckConstraint("ck_inventory_transfer_receipt_quantity", "\"Quantity\" > 0");
            table.HasCheckConstraint("ck_inventory_transfer_receipt_entered_quantity", "\"EnteredQuantity\" > 0");
            table.HasCheckConstraint("ck_inventory_transfer_receipt_conversion_factor", "\"ConversionFactorToBase\" > 0");
            table.HasCheckConstraint("ck_inventory_transfer_receipt_conversion_math",
                "ROUND(\"EnteredQuantity\" * \"ConversionFactorToBase\", 6) = \"Quantity\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 6);
        builder.Property(x => x.EnteredQuantity).HasPrecision(18, 6);
        builder.Property(x => x.ConversionFactorToBase).HasPrecision(28, 12);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.ActorIdentifier).HasMaxLength(200);
        builder.HasIndex(x => new { x.TransferId, x.EffectiveDate });
        builder.HasIndex(x => x.InventoryTransactionId).IsUnique()
            .HasFilter("\"InventoryTransactionId\" IS NOT NULL");
        builder.HasOne(x => x.Transfer).WithMany().HasForeignKey(x => x.TransferId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TransactionUnit).WithMany().HasForeignKey(x => x.TransactionUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InventoryTransaction).WithMany().HasForeignKey(x => x.InventoryTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
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
