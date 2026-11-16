namespace Hadron.Erp.Api.Modules.Analytics.Application;

public static class AnalyticsMath
{
    public static decimal PercentileCont(IEnumerable<int> source, decimal percentile)
    {
        if (percentile is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
        var values = source.Order().Select(x => (decimal)x).ToArray();
        if (values.Length == 0) throw new InvalidOperationException("EMPTY_SAMPLE");
        var position = (values.Length - 1) * percentile;
        var lower = (int)decimal.Floor(position);
        var upper = (int)decimal.Ceiling(position);
        if (lower == upper) return values[lower];
        var fraction = position - lower;
        return values[lower] + ((values[upper] - values[lower]) * fraction);
    }

    public static PercentileStatistics? Statistics(IReadOnlyCollection<int> values, string window)
    {
        if (values.Count == 0) return null;
        var p80 = PercentileCont(values, 0.80m);
        return new(values.Count, values.Average(x => (decimal)x), PercentileCont(values, 0.50m), p80,
            PercentileCont(values, 0.90m), values.Min(), values.Max(),
            values.Count >= 5 ? (int)decimal.Ceiling(p80) : null, window);
    }

    public static decimal? Ape(decimal forecast, decimal actual) => actual == 0 ? null :
        decimal.Abs(forecast - actual) / decimal.Abs(actual) * 100m;

    public static decimal? Wape(IEnumerable<(decimal Forecast, decimal Actual)> rows)
    {
        var materialized = rows.ToArray();
        var denominator = materialized.Sum(x => decimal.Abs(x.Actual));
        return denominator == 0 ? null : materialized.Sum(x => decimal.Abs(x.Forecast - x.Actual)) /
            denominator * 100m;
    }

    public static decimal? Bias(IEnumerable<(decimal Forecast, decimal Actual)> rows)
    {
        var materialized = rows.ToArray();
        var denominator = materialized.Sum(x => decimal.Abs(x.Actual));
        return denominator == 0 ? null : materialized.Sum(x => x.Forecast - x.Actual) /
            denominator * 100m;
    }
}
