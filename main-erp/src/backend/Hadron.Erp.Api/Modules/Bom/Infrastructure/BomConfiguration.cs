using Hadron.Erp.Api.Modules.Bom.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Bom.Infrastructure;

internal sealed class BomRevisionConfiguration : IEntityTypeConfiguration<BomRevision>
{
    public void Configure(EntityTypeBuilder<BomRevision> b)
    {
        b.ToTable("bom_revisions", "engineering", t =>
        {
            t.HasCheckConstraint("ck_bom_revisions_status", "\"Status\" IN ('DRAFT','APPROVED','OBSOLETE')");
            t.HasCheckConstraint("ck_bom_revisions_base_output", "\"BaseOutputQty\" > 0");
            t.HasCheckConstraint("ck_bom_revisions_version", "\"Version\" >= 1");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.RevisionCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasMaxLength(16).IsRequired();
        b.Property(x => x.BaseOutputQty).HasPrecision(18, 6);
        b.Property(x => x.LegacyEffectiveFrom).HasColumnName("EffectiveFrom");
        b.Property(x => x.LegacyEffectiveTo).HasColumnName("EffectiveTo");
        b.Property(x => x.RevisionNotes).HasMaxLength(2000);
        b.Property(x => x.ApprovedByActorId).HasMaxLength(200);
        b.Property(x => x.ObsoletedByActorId).HasMaxLength(200);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => x.RevisionCode).IsUnique();
        b.HasIndex(x => new { x.ParentItemId, x.Status });
        b.HasIndex(x => x.ParentItemId).IsUnique().HasFilter("\"Status\" = 'APPROVED'")
            .HasDatabaseName("UX_bom_revisions_one_approved_parent");
        b.HasOne(x => x.ParentItem).WithMany().HasForeignKey(x => x.ParentItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ParentBaseUnit).WithMany().HasForeignKey(x => x.ParentBaseUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CopiedFromRevision).WithMany().HasForeignKey(x => x.CopiedFromRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BomLineConfiguration : IEntityTypeConfiguration<BomLine>
{
    public void Configure(EntityTypeBuilder<BomLine> b)
    {
        b.ToTable("bom_lines", "engineering", t =>
        {
            t.HasCheckConstraint("ck_bom_lines_usage", "\"UsageQty\" > 0");
            t.HasCheckConstraint("ck_bom_lines_scrap", "\"ScrapOverridePercent\" IS NULL OR \"ScrapOverridePercent\" BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_bom_lines_sort", "\"SortOrder\" >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.UsageQty).HasPrecision(18, 6);
        b.Property(x => x.ScrapOverridePercent).HasPrecision(5, 2);
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.BomRevisionId, x.ChildItemId }).IsUnique();
        b.HasIndex(x => x.ChildItemId);
        b.HasOne(x => x.BomRevision).WithMany(x => x.Lines).HasForeignKey(x => x.BomRevisionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ChildItem).WithMany().HasForeignKey(x => x.ChildItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ChildBaseUnit).WithMany().HasForeignKey(x => x.ChildBaseUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BomItemApplicabilityConfiguration : IEntityTypeConfiguration<BomItemApplicability>
{
    public void Configure(EntityTypeBuilder<BomItemApplicability> b)
    {
        b.ToTable("bom_item_applicabilities", "engineering", table =>
            table.HasCheckConstraint("ck_bom_item_applicabilities_version", "\"Version\" >= 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.HasIndex(x => new { x.ParentItemId, x.ChildItemId }).IsUnique();
        b.HasIndex(x => new { x.ChildItemId, x.IsActive });
        b.HasIndex(x => new { x.ParentItemId, x.IsActive });
        b.HasOne(x => x.ParentItem).WithMany().HasForeignKey(x => x.ParentItemId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ChildItem).WithMany().HasForeignKey(x => x.ChildItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
