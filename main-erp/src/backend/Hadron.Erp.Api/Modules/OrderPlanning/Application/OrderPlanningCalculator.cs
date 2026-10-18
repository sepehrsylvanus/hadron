using Hadron.Erp.Api.Modules.OrderPlanning.Domain;

namespace Hadron.Erp.Api.Modules.OrderPlanning.Application;

public sealed record CandidateEvaluation(Guid SourcingOptionId, string SourcingNumber, bool IsPreferred,
    bool IsEligible, int? TotalLeadTimeDays);
public sealed record SizingResult(decimal MinimumBaseline, decimal BaseRecommendedQty,
    decimal SystemRecommendedQty, DateOnly DecisionHorizonDate, bool HorizonSufficient);

public sealed class OrderPlanningCalculator
{
    public const string AlgorithmVersion = "ORDER-PLANNING-V1";

    public CandidateEvaluation? SelectCandidate(IReadOnlyList<CandidateEvaluation> candidates,
        DateOnly evaluationDate, DateOnly needByDate)
    {
        var eligible = candidates.Where(x => x.IsEligible && x.TotalLeadTimeDays.HasValue).ToList();
        var onTime = eligible.Where(x => evaluationDate.AddDays(x.TotalLeadTimeDays!.Value) <= needByDate).ToList();
        var preferred = onTime.Where(x => x.IsPreferred).OrderBy(x => x.TotalLeadTimeDays)
            .ThenBy(x => x.SourcingNumber, StringComparer.Ordinal).ThenBy(x => x.SourcingOptionId).FirstOrDefault();
        return preferred ?? onTime.OrderBy(x => x.TotalLeadTimeDays).ThenBy(x => x.SourcingNumber, StringComparer.Ordinal)
            .ThenBy(x => x.SourcingOptionId).FirstOrDefault() ??
            eligible.OrderBy(x => x.TotalLeadTimeDays).ThenBy(x => x.SourcingNumber, StringComparer.Ordinal)
                .ThenBy(x => x.SourcingOptionId).FirstOrDefault();
    }

    public SizingResult Size(DateOnly targetReceiptDate, int safeStartDays, DateOnly horizonEnd,
        decimal safetyStock, decimal? moq, IReadOnlyList<(DateOnly Date, decimal FirmProjected)> timeline)
    {
        var decisionHorizon = targetReceiptDate.AddDays(safeStartDays);
        if (decisionHorizon > horizonEnd) return new(0, 0, 0, decisionHorizon, false);
        var ordered = timeline.OrderBy(x => x.Date).ToList();
        var starting = ordered.LastOrDefault(x => x.Date <= targetReceiptDate).FirmProjected;
        var values = new List<decimal> { starting };
        values.AddRange(ordered.Where(x => x.Date > targetReceiptDate && x.Date <= decisionHorizon)
            .Select(x => x.FirmProjected));
        var minimum = values.Min();
        var baseQty = Math.Max(safetyStock - minimum, 0m);
        return new(minimum, baseQty, Math.Max(baseQty, moq ?? 0m), decisionHorizon, true);
    }

    public static (string Priority, string NextAction) Priority(DateOnly evaluationDate,
        DateOnly needByDate, DateOnly orderByDate, int criticalDays, bool canMeetNeedBy)
    {
        if (!canMeetNeedBy) return (RecommendationPriorities.Emergency, RecommendationNextActions.EmergencyReview);
        var days = orderByDate.DayNumber - evaluationDate.DayNumber;
        if (days <= 0) return (RecommendationPriorities.DueNow, RecommendationNextActions.OrderNow);
        return days <= criticalDays
            ? (RecommendationPriorities.Upcoming, RecommendationNextActions.OrderByDate)
            : (RecommendationPriorities.Planned, RecommendationNextActions.OrderByDate);
    }

    public static bool MoqCausesExcess(DateOnly targetReceiptDate, int excessStartDays,
        DateOnly horizonEnd, decimal systemRecommendedQty,
        IReadOnlyList<(DateOnly Date, decimal FirmProjected)> timeline)
    {
        var proofDate = targetReceiptDate.AddDays(excessStartDays + 1);
        if (proofDate > horizonEnd) return false;
        var ordered = timeline.OrderBy(x => x.Date).ToList();
        var starting = ordered.LastOrDefault(x => x.Date <= targetReceiptDate).FirmProjected;
        var minimum = new[] { starting }.Concat(ordered
            .Where(x => x.Date > targetReceiptDate && x.Date <= proofDate)
            .Select(x => x.FirmProjected)).Min();
        return minimum + systemRecommendedQty >= 0;
    }
}
