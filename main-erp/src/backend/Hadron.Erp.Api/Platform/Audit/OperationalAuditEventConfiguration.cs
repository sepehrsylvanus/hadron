using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Platform.Audit;

internal sealed class OperationalAuditEventConfiguration : IEntityTypeConfiguration<OperationalAuditEvent>
{
    public void Configure(EntityTypeBuilder<OperationalAuditEvent> builder)
    {
        builder.ToTable("operational_audit_events", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(200);
        builder.Property(x => x.CorrelationId).HasMaxLength(200);
        builder.Property(x => x.PreviousValuesJson).HasColumnType("jsonb");
        builder.Property(x => x.CurrentValuesJson).HasColumnType("jsonb");
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.Source).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.OccurredAtUtc });
        builder.HasIndex(x => x.CorrelationId);
    }
}
