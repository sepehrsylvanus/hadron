using Hadron.Erp.Api.Modules.Mrp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.Mrp.Infrastructure;

public sealed class MrpRefreshOutboxConfiguration : IEntityTypeConfiguration<MrpRefreshOutboxMessage>
{
    public void Configure(EntityTypeBuilder<MrpRefreshOutboxMessage> builder)
    {
        builder.ToTable("mrp_refresh_outbox", "planning", table =>
        {
            table.HasCheckConstraint("CK_mrp_refresh_outbox_status",
                "\"Status\" IN ('PENDING','PROCESSING','COMPLETED','FAILED')");
            table.HasCheckConstraint("CK_mrp_refresh_outbox_attempt_count", "\"AttemptCount\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(120).IsRequired();
        builder.Property(x => x.SourceEntityTypes).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ErrorCode).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.Status, x.AvailableAtUtc });
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => x.RunId);
    }
}
