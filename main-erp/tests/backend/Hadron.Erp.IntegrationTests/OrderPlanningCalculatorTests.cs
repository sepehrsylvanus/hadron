using Hadron.Erp.Api.Modules.OrderPlanning.Application;
using Hadron.Erp.Api.Modules.OrderPlanning.Domain;

namespace Hadron.Erp.IntegrationTests;

public sealed class OrderPlanningCalculatorTests
{
    private readonly OrderPlanningCalculator calculator = new();
    private static readonly DateOnly EvaluationDate = new(2026, 8, 1); // 1405/05/10
    private static readonly DateOnly NeedBy = new(2026, 8, 5); // 1405/05/14

    [Fact]
    public void Gd04_selects_fastest_on_time_when_preferred_is_late()
    {
        var s1 = new CandidateEvaluation(Guid.NewGuid(), "SRC-0001", true, true, 5);
        var s2 = new CandidateEvaluation(Guid.NewGuid(), "SRC-0002", false, true, 3);

        var selected = calculator.SelectCandidate([s1, s2], EvaluationDate, NeedBy);

        Assert.Equal(s2.SourcingOptionId, selected!.SourcingOptionId);
    }

    [Fact]
    public void Preferred_on_time_source_wins_even_when_another_source_is_faster()
    {
        var preferred = new CandidateEvaluation(Guid.NewGuid(), "SRC-0002", true, true, 3);
        var faster = new CandidateEvaluation(Guid.NewGuid(), "SRC-0001", false, true, 1);
        Assert.Equal(preferred.SourcingOptionId,
            calculator.SelectCandidate([faster, preferred], EvaluationDate, NeedBy)!.SourcingOptionId);
    }

    [Fact]
    public void Source_tie_break_is_lead_then_number_then_uuid_and_never_price()
    {
        var lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var selected = calculator.SelectCandidate([
            new(higherId, "SRC-0002", false, true, 3),
            new(lowerId, "SRC-0001", false, true, 3)], EvaluationDate, NeedBy);
        Assert.Equal(lowerId, selected!.SourcingOptionId);
    }

    [Fact]
    public void Fastest_valid_late_source_is_selected_when_none_can_meet_need_by()
    {
        var slow = new CandidateEvaluation(Guid.NewGuid(), "SRC-0001", true, true, 8);
        var fastest = new CandidateEvaluation(Guid.NewGuid(), "SRC-0002", false, true, 6);
        Assert.Equal(fastest.SourcingOptionId,
            calculator.SelectCandidate([slow, fastest], EvaluationDate, NeedBy)!.SourcingOptionId);
    }

    [Fact]
    public void Gd04_uses_factual_firm_projected_and_applies_moq_after_base_quantity()
    {
        var result = calculator.Size(NeedBy, 2, NeedBy.AddDays(2), 0, 80,
            [(NeedBy, -15), (NeedBy.AddDays(1), -35), (NeedBy.AddDays(2), -55)]);
        Assert.True(result.HorizonSufficient);
        Assert.Equal(NeedBy.AddDays(2), result.DecisionHorizonDate);
        Assert.Equal(-55, result.MinimumBaseline);
        Assert.Equal(55, result.BaseRecommendedQty);
        Assert.Equal(80, result.SystemRecommendedQty);
        Assert.Equal(560, result.SystemRecommendedQty * 7m); // fixed canonical CNY value, not calculator-derived expectation
    }

    [Fact]
    public void Gd04b_safety_stock_is_the_floor()
    {
        var result = calculator.Size(NeedBy, 2, NeedBy.AddDays(2), 10, 80,
            [(NeedBy, -15), (NeedBy.AddDays(1), -35), (NeedBy.AddDays(2), -55)]);
        Assert.Equal(65, result.BaseRecommendedQty);
        Assert.Equal(80, result.SystemRecommendedQty);
    }

    [Fact]
    public void Gd08_fixed_recommendation_is_30_base_and_50_system()
    {
        var evaluationDate = new DateOnly(2026, 8, 18); // 1405/05/27
        var needBy = new DateOnly(2026, 8, 20); // 1405/05/29
        var orderBy = new DateOnly(2026, 8, 19); // 1405/05/28
        var decisionHorizon = new DateOnly(2026, 8, 21); // 1405/05/30
        var result = calculator.Size(needBy, 1, decisionHorizon, 0, 50,
            [(needBy, -10), (decisionHorizon, -30)]);
        var priority = OrderPlanningCalculator.Priority(evaluationDate, needBy, orderBy, 2, true);

        Assert.Equal(new DateOnly(2026, 8, 20), needBy);
        Assert.Equal(new DateOnly(2026, 8, 19), orderBy);
        Assert.Equal(new DateOnly(2026, 8, 21), result.DecisionHorizonDate);
        Assert.Equal(-30, result.MinimumBaseline);
        Assert.Equal(30, result.BaseRecommendedQty);
        Assert.Equal(50, result.SystemRecommendedQty);
        Assert.Equal(RecommendationPriorities.Upcoming, priority.Priority);
    }

    [Fact]
    public void Event_sparse_timeline_carries_last_factual_balance_forward()
    {
        var result = calculator.Size(NeedBy, 2, NeedBy.AddDays(2), 5, 0,
            [(NeedBy.AddDays(-2), -7)]);
        Assert.Equal(-7, result.MinimumBaseline);
        Assert.Equal(12, result.BaseRecommendedQty);
        Assert.Equal(12, result.SystemRecommendedQty);
    }

    [Fact]
    public void Insufficient_horizon_does_not_publish_normal_quantity()
    {
        var result = calculator.Size(NeedBy, 3, NeedBy.AddDays(2), 0, 80, [(NeedBy, -15)]);
        Assert.False(result.HorizonSufficient);
        Assert.Equal(0, result.SystemRecommendedQty);
    }

    [Theory]
    [InlineData(null, 12)]
    [InlineData(0, 12)]
    [InlineData(20, 20)]
    public void Moq_null_zero_and_positive_are_applied_only_after_firm_projected_sizing(
        int? moq, int expected)
    {
        var result = calculator.Size(NeedBy, 1, NeedBy.AddDays(1), 5, moq,
            [(NeedBy, -7), (NeedBy.AddDays(1), -7)]);
        Assert.Equal(12, result.BaseRecommendedQty);
        Assert.Equal((decimal)expected, result.SystemRecommendedQty);
    }

    [Fact]
    public void Moq_excess_is_proven_from_target_receipt_anchor_only_inside_known_horizon()
    {
        var timeline = new List<(DateOnly, decimal)>
        {
            (NeedBy, -10), (NeedBy.AddDays(1), -30), (NeedBy.AddDays(3), -40)
        };
        Assert.True(OrderPlanningCalculator.MoqCausesExcess(NeedBy, 2, NeedBy.AddDays(3), 50, timeline));
        Assert.False(OrderPlanningCalculator.MoqCausesExcess(NeedBy, 3, NeedBy.AddDays(3), 50, timeline));
        Assert.False(OrderPlanningCalculator.MoqCausesExcess(NeedBy, 2, NeedBy.AddDays(3), 20, timeline));
    }

    [Theory]
    [InlineData(-1, true, "P1_DUE_NOW", "ORDER_NOW")]
    [InlineData(0, true, "P1_DUE_NOW", "ORDER_NOW")]
    [InlineData(1, true, "P2_UPCOMING", "ORDER_BY_DATE")]
    [InlineData(3, true, "P3_PLANNED", "ORDER_BY_DATE")]
    [InlineData(3, false, "P0_EMERGENCY", "EMERGENCY_REVIEW")]
    public void Priority_uses_generation_evaluation_date(int orderByOffset, bool canMeet,
        string expectedPriority, string expectedAction)
    {
        var result = OrderPlanningCalculator.Priority(EvaluationDate, NeedBy,
            EvaluationDate.AddDays(orderByOffset), 2, canMeet);
        Assert.Equal(expectedPriority, result.Priority);
        Assert.Equal(expectedAction, result.NextAction);
    }
}
