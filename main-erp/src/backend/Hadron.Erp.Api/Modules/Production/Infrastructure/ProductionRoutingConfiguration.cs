using Hadron.Erp.Api.Modules.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Production.Infrastructure;

internal sealed class ProductionWorkCenterConfiguration : IEntityTypeConfiguration<ProductionWorkCenter>
{
    public void Configure(EntityTypeBuilder<ProductionWorkCenter> builder)
    {
        builder.ToTable("work_centers", "production", table =>
        {
            table.HasCheckConstraint("ck_production_work_center_number", "\"BusinessNumber\" ~ '^WC-[0-9]{4,}$'");
            table.HasCheckConstraint("ck_production_work_center_week", "\"WorkingWeekMask\" BETWEEN 1 AND 127");
            table.HasCheckConstraint("ck_production_work_center_version", "\"Version\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NameFa).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.BusinessNumber).IsUnique();
        builder.HasIndex(x => new { x.OrganizationalUnitId, x.IsActive });
        builder.HasOne(x => x.OrganizationalUnit).WithMany().HasForeignKey(x => x.OrganizationalUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ManufacturingRouteConfiguration : IEntityTypeConfiguration<ManufacturingRoute>
{
    public void Configure(EntityTypeBuilder<ManufacturingRoute> builder)
    {
        builder.ToTable("manufacturing_routes", "production", table =>
        {
            table.HasCheckConstraint("ck_manufacturing_route_number", "\"BusinessNumber\" ~ '^RTE-[0-9]{4,}$'");
            table.HasCheckConstraint("ck_manufacturing_route_status", "\"Status\" IN ('DRAFT','APPROVED','OBSOLETE')");
            table.HasCheckConstraint("ck_manufacturing_route_revision", "\"RevisionNumber\" >= 1");
            table.HasCheckConstraint("ck_manufacturing_route_version", "\"Version\" >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.BusinessNumber).IsUnique();
        builder.HasIndex(x => new { x.ItemId, x.RevisionNumber }).IsUnique();
        builder.HasIndex(x => x.ItemId).IsUnique().HasFilter("\"Status\" = 'APPROVED'")
            .HasDatabaseName("ux_manufacturing_route_current_item");
        builder.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ManufacturingRouteOperationConfiguration
    : IEntityTypeConfiguration<ManufacturingRouteOperation>
{
    public void Configure(EntityTypeBuilder<ManufacturingRouteOperation> builder)
    {
        builder.ToTable("manufacturing_route_operations", "production", table =>
        {
            table.HasCheckConstraint("ck_manufacturing_route_operation_sequence", "\"Sequence\" >= 10");
            table.HasCheckConstraint("ck_manufacturing_route_operation_days", "\"PreparationWorkingDays\" >= 0 AND \"ExecutionWorkingDays\" >= 0 AND (\"PreparationWorkingDays\" + \"ExecutionWorkingDays\") > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.RouteId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.Route).WithMany(x => x.Operations).HasForeignKey(x => x.RouteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WorkCenter).WithMany().HasForeignKey(x => x.WorkCenterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
