using Hadron.Erp.Api.Modules.CashFlow.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.CashFlow.Infrastructure;

internal sealed class MaterialCostBookConfiguration : IEntityTypeConfiguration<MaterialCostBook>
{
    public void Configure(EntityTypeBuilder<MaterialCostBook> builder)
    {
        builder.ToTable("material_cost_books", "cash_flow");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BookNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CreatedByIdentifier).HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(x => x.BookNumber).IsUnique();
    }
}

internal sealed class MaterialCostBookRevisionConfiguration
    : IEntityTypeConfiguration<MaterialCostBookRevision>
{
    public void Configure(EntityTypeBuilder<MaterialCostBookRevision> builder)
    {
        builder.ToTable("material_cost_book_revisions", "cash_flow", table =>
        {
            table.HasCheckConstraint("ck_material_cost_book_revisions_revision", "\"Revision\" >= 1");
            table.HasCheckConstraint("ck_material_cost_book_revisions_status",
                "\"Status\" IN ('DRAFT','PUBLISHED')");
            table.HasCheckConstraint("ck_material_cost_book_revisions_version", "\"Version\" >= 1");
            table.HasCheckConstraint("ck_material_cost_book_revisions_publish_state",
                "(\"Status\" = 'DRAFT' AND \"PublishedAtUtc\" IS NULL AND \"PublishReason\" IS NULL) OR " +
                "(\"Status\" = 'PUBLISHED' AND \"PublishedAtUtc\" IS NOT NULL AND NULLIF(BTRIM(\"PublishReason\"), '') IS NOT NULL)");
            table.HasCheckConstraint("ck_material_cost_book_revisions_not_self_superseding",
                "\"SupersedesRevisionId\" IS NULL OR \"SupersedesRevisionId\" <> \"Id\"");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NameFa).HasMaxLength(250).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(250);
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ChangeReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedByIdentifier).HasMaxLength(200);
        builder.Property(x => x.UpdatedByIdentifier).HasMaxLength(200);
        builder.Property(x => x.PublishedByIdentifier).HasMaxLength(200);
        builder.Property(x => x.PublishReason).HasMaxLength(1000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.MaterialCostBook).WithMany(x => x.Revisions)
            .HasForeignKey(x => x.MaterialCostBookId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SupersedesRevision).WithMany()
            .HasForeignKey(x => x.SupersedesRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ClientRequestId).IsUnique();
        builder.HasIndex(x => new { x.MaterialCostBookId, x.Revision }).IsUnique();
        builder.HasIndex(x => x.MaterialCostBookId).IsUnique()
            .HasFilter("\"Status\" = 'DRAFT'");
        builder.HasIndex(x => x.SupersedesRevisionId).IsUnique()
            .HasFilter("\"SupersedesRevisionId\" IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.EffectiveDate, x.Revision });
    }
}

internal sealed class MaterialCostBookLineConfiguration
    : IEntityTypeConfiguration<MaterialCostBookLine>
{
    public void Configure(EntityTypeBuilder<MaterialCostBookLine> builder)
    {
        builder.ToTable("material_cost_book_lines", "cash_flow", table =>
        {
            table.HasCheckConstraint("ck_material_cost_book_lines_price", "\"OriginalPriceAmount\" > 0");
            table.HasCheckConstraint("ck_material_cost_book_lines_basis", "\"PriceBasisQuantity\" > 0");
            table.HasCheckConstraint("ck_material_cost_book_lines_factor", "\"PriceUnitFactorToBase\" > 0");
            table.HasCheckConstraint("ck_material_cost_book_lines_currency_precision",
                "\"CurrencyDecimalPrecision\" BETWEEN 0 AND 12");
            table.HasCheckConstraint("ck_material_cost_book_lines_supplier_snapshot",
                "(\"SupplierId\" IS NULL AND \"SupplierNumber\" IS NULL AND \"SupplierName\" IS NULL) OR " +
                "(\"SupplierId\" IS NOT NULL AND NULLIF(BTRIM(\"SupplierNumber\"), '') IS NOT NULL AND NULLIF(BTRIM(\"SupplierName\"), '') IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ItemCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ItemNameFa).HasMaxLength(250).IsRequired();
        builder.Property(x => x.ItemNameEn).HasMaxLength(250);
        builder.Property(x => x.CurrencyCode).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CurrencyNameFa).HasMaxLength(120).IsRequired();
        builder.Property(x => x.CurrencyNameEn).HasMaxLength(120);
        builder.Property(x => x.OriginalPriceAmount).HasPrecision(28, 12);
        builder.Property(x => x.PriceBasisQuantity).HasPrecision(28, 12);
        builder.Property(x => x.PriceUnitCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PriceUnitNameFa).HasMaxLength(120).IsRequired();
        builder.Property(x => x.PriceUnitNameEn).HasMaxLength(120);
        builder.Property(x => x.PriceUnitFactorToBase).HasPrecision(28, 12);
        builder.Property(x => x.ItemBaseUnitCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ItemBaseUnitNameFa).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ItemBaseUnitNameEn).HasMaxLength(120);
        builder.Property(x => x.SupplierNumber).HasMaxLength(64);
        builder.Property(x => x.SupplierName).HasMaxLength(250);
        builder.Property(x => x.SourceReference).HasMaxLength(250);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Ignore(x => x.NormalizedUnitPrice);
        builder.HasOne(x => x.MaterialCostBookRevision).WithMany(x => x.Lines)
            .HasForeignKey(x => x.MaterialCostBookRevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Currency).WithMany().HasForeignKey(x => x.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PriceUnit).WithMany().HasForeignKey(x => x.PriceUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ItemBaseUnit).WithMany().HasForeignKey(x => x.ItemBaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.MaterialCostBookRevisionId, x.ItemId }).IsUnique();
        builder.HasIndex(x => x.ItemId);
        builder.HasIndex(x => x.CurrencyId);
        builder.HasIndex(x => x.SupplierId);
    }
}

internal sealed class MaterialCostBookOperationReceiptConfiguration
    : IEntityTypeConfiguration<MaterialCostBookOperationReceipt>
{
    public void Configure(EntityTypeBuilder<MaterialCostBookOperationReceipt> builder)
    {
        builder.ToTable("material_cost_book_operation_receipts", "cash_flow", table =>
        {
            table.HasCheckConstraint("ck_material_cost_book_operation_receipts_type",
                "\"OperationType\" IN ('UPDATE_DRAFT','UPSERT_LINE','REMOVE_LINE','PUBLISH')");
            table.HasCheckConstraint("ck_material_cost_book_operation_receipts_version",
                "\"ResultVersion\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.MaterialCostBookRevision).WithMany()
            .HasForeignKey(x => x.MaterialCostBookRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.OperationType, x.ClientRequestId }).IsUnique();
        builder.HasIndex(x => x.MaterialCostBookRevisionId);
    }
}
