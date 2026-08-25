using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hadron.Erp.Api.Modules.OrderPlanning.Infrastructure;

internal sealed class OrderPlanningGenerationConfiguration : IEntityTypeConfiguration<OrderPlanningGeneration>
{
    public void Configure(EntityTypeBuilder<OrderPlanningGeneration> b)
    {
        b.ToTable("order_planning_generations", "planning", t =>
            t.HasCheckConstraint("ck_order_planning_generation_status", "\"Status\" IN ('RUNNING','COMPLETED','FAILED')"));
        b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.FailureCode).HasMaxLength(100); b.Property(x => x.FailureMessage).HasMaxLength(2000);
        b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.HasIndex(x => new { x.RunId, x.GenerationSequence }).IsUnique();
        b.HasIndex(x => x.RunId).IsUnique().HasFilter("\"IsCurrent\"");
        b.HasOne<MrpRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderRecommendationConfiguration : IEntityTypeConfiguration<OrderRecommendation>
{
    public void Configure(EntityTypeBuilder<OrderRecommendation> b)
    {
        b.ToTable("order_recommendations", "planning", t =>
        {
            t.HasCheckConstraint("ck_order_recommendation_state", "\"State\" IN ('READY','BLOCKED')");
            t.HasCheckConstraint("ck_order_recommendation_timing", "\"TimingState\" IN ('ON_TIME','LATE')");
            t.HasCheckConstraint("ck_order_recommendation_qty", "\"NetRequirementQty\" > 0 AND (\"BaseRecommendedQty\" IS NULL OR \"BaseRecommendedQty\" >= 0) AND (\"SystemRecommendedQty\" IS NULL OR \"SystemRecommendedQty\" > 0)");
        });
        b.HasKey(x => x.Id); b.Property(x => x.ItemCode).HasMaxLength(32).IsRequired();
        b.Property(x => x.ItemType).HasMaxLength(20).IsRequired(); b.Property(x => x.ItemNameFa).HasMaxLength(200).IsRequired();
        b.Property(x => x.ItemNameEn).HasMaxLength(200); b.Property(x => x.UnitCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.RouteOrigin).HasMaxLength(32).IsRequired(); b.Property(x => x.State).HasMaxLength(20).IsRequired();
        b.Property(x => x.TimingState).HasMaxLength(20).IsRequired(); b.Property(x => x.Priority).HasMaxLength(30);
        b.Property(x => x.NextAction).HasMaxLength(40).IsRequired(); b.Property(x => x.ReasonCodes).HasMaxLength(1000).IsRequired();
        b.Property(x => x.SourceSelectionReason).HasMaxLength(100).IsRequired();
        b.Property(x => x.CalculationTrace).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.AlgorithmVersion).HasMaxLength(64).IsRequired();
        foreach (var name in new[] { nameof(OrderRecommendation.NetRequirementQty), nameof(OrderRecommendation.SafetyStockQty),
                     nameof(OrderRecommendation.MinimumBaseline), nameof(OrderRecommendation.BaseRecommendedQty),
                     nameof(OrderRecommendation.SystemRecommendedQty), nameof(OrderRecommendation.EstimatedValue) })
            b.Property(name).HasPrecision(28, 12);
        b.HasIndex(x => new { x.GenerationId, x.ItemId }).IsUnique();
        b.HasIndex(x => new { x.GenerationId, x.State, x.Priority, x.NeedByDate });
        b.HasOne<OrderPlanningGeneration>().WithMany().HasForeignKey(x => x.GenerationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OrderRecommendationCandidate>().WithMany()
            .HasForeignKey(x => new { x.SelectedCandidateId, x.GenerationId, x.ItemId })
            .HasPrincipalKey(x => new { x.Id, x.GenerationId, x.ItemId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderRecommendationCandidateConfiguration : IEntityTypeConfiguration<OrderRecommendationCandidate>
{
    public void Configure(EntityTypeBuilder<OrderRecommendationCandidate> b)
    {
        b.ToTable("order_recommendation_candidates", "planning"); b.HasKey(x => x.Id);
        b.Property(x => x.SourcingNumber).HasMaxLength(32).IsRequired(); b.Property(x => x.SupplierNumber).HasMaxLength(32).IsRequired();
        b.Property(x => x.SupplierNameFa).HasMaxLength(200).IsRequired(); b.Property(x => x.SupplierNameEn).HasMaxLength(200);
        b.Property(x => x.ReasonCodes).HasMaxLength(1000).IsRequired(); b.Property(x => x.ShippingModeCode).HasMaxLength(50);
        b.Property(x => x.ShippingModeNameFa).HasMaxLength(200); b.Property(x => x.ShippingModeNameEn).HasMaxLength(200);
        b.Property(x => x.IncotermCode).HasMaxLength(50); b.Property(x => x.IncotermNameFa).HasMaxLength(200);
        b.Property(x => x.IncotermNameEn).HasMaxLength(200); b.Property(x => x.CurrencyCode).HasMaxLength(20);
        b.Property(x => x.CurrencyNameFa).HasMaxLength(200); b.Property(x => x.CurrencyNameEn).HasMaxLength(200);
        b.Property(x => x.CreditAnchor).HasMaxLength(40);
        b.Property(x => x.PaymentTermCode).HasMaxLength(50); b.Property(x => x.PaymentTermNameFa).HasMaxLength(200);
        b.Property(x => x.PaymentTermNameEn).HasMaxLength(200);
        foreach (var name in new[] { nameof(OrderRecommendationCandidate.MinimumOrderQuantity),
                     nameof(OrderRecommendationCandidate.UnitPrice), nameof(OrderRecommendationCandidate.AdvancePercent),
                     nameof(OrderRecommendationCandidate.BeforeShipmentPercent), nameof(OrderRecommendationCandidate.CreditPercent) })
            b.Property(name).HasPrecision(28, 12);
        b.HasIndex(x => new { x.GenerationId, x.ItemId, x.SourcingOptionId }).IsUnique();
        b.HasAlternateKey(x => new { x.Id, x.GenerationId, x.ItemId });
        b.HasOne<OrderPlanningGeneration>().WithMany().HasForeignKey(x => x.GenerationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderRecommendationDecisionConfiguration : IEntityTypeConfiguration<OrderRecommendationDecision>
{
    public void Configure(EntityTypeBuilder<OrderRecommendationDecision> b)
    {
        b.ToTable("order_recommendation_decisions", "planning", t =>
        {
            t.HasCheckConstraint("ck_order_decision_type", "\"DecisionType\" IN ('ACCEPT','MODIFY','REJECT','DEFER')");
            t.HasCheckConstraint("ck_order_decision_reason", "\"DecisionType\" = 'ACCEPT' OR (\"Reason\" IS NOT NULL AND length(btrim(\"Reason\")) > 0)");
            t.HasCheckConstraint("ck_order_decision_review", "\"DecisionType\" <> 'DEFER' OR \"ReviewDate\" IS NOT NULL");
            t.HasCheckConstraint("ck_order_decision_version_positive", "\"DecisionVersion\" > 0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.DecisionType).HasMaxLength(20).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.ActorIdentifier).HasMaxLength(200);
        b.Property(x => x.FinalSourcingNumber).HasMaxLength(32); b.Property(x => x.FinalSupplierNumber).HasMaxLength(32);
        b.Property(x => x.FinalSupplierNameFa).HasMaxLength(200); b.Property(x => x.FinalCurrencyCode).HasMaxLength(20);
        b.Property(x => x.WarningCodes).HasMaxLength(500).IsRequired();
        b.Property(x => x.FinalSourceSnapshot).HasColumnType("jsonb");
        foreach (var name in new[] { nameof(OrderRecommendationDecision.FinalQty), nameof(OrderRecommendationDecision.FinalUnitPrice),
                     nameof(OrderRecommendationDecision.FinalMinimumOrderQuantity) }) b.Property(name).HasPrecision(28, 12);
        b.HasIndex(x => new { x.RecommendationId, x.DecisionVersion }).IsUnique();
        b.HasOne<OrderRecommendation>().WithMany().HasForeignKey(x => x.RecommendationId).OnDelete(DeleteBehavior.Restrict);
    }
}
