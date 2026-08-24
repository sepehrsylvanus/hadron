using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Domain;

namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed record MrpItemSnapshot(Guid ItemId, string ItemCode, string ItemType, string NameFa, string? NameEn,
    bool IsActive, Guid BaseUnitId, string? SupplyMethod, decimal SafetyStockQty, decimal DefaultScrapPercent);
public sealed record MrpProductDemand(Guid ProductId, Guid BusinessPeriodId, Guid ForecastSeriesId,
    Guid ForecastRevisionId, DateOnly Date, decimal Quantity, decimal PeriodForecastQty, decimal ActualMtd, Guid UnitId);
public sealed record MrpBomEdge(Guid RevisionId, string RevisionCode, Guid LineId, Guid ParentItemId,
    Guid ChildItemId, Guid ParentBaseUnitId, Guid ChildBaseUnitId, decimal BaseOutputQty, decimal UsageQty,
    decimal? ScrapOverridePercent, DateOnly EffectiveFrom, DateOnly? EffectiveTo);
public sealed record MrpInventoryPosition(Guid ItemId, Guid WarehouseId, string WarehouseCode,
    bool WarehouseIsActive, Guid UnitId, decimal OnHandQty);
public sealed record MrpFirmSupply(Guid ItemId, Guid PurchaseOrderId, string PurchaseOrderNumber,
    Guid PurchaseOrderLineId, Guid DeliveryScheduleId, decimal OpenQty, DateOnly? Eta,
    string EtaSource, string EtaStatus, string PurchaseOrderStatus);
public sealed record MrpRouteAvailability(Guid ItemId, DateOnly Date, bool BomReady, bool ExternalReady);
public sealed record MrpRouteChoice(Guid ItemId, string Route, string Source);
public sealed record MrpCalculationException(Guid? ItemId, DateOnly? Date, string Code, string Severity,
    bool AffectsDemand, string Message, string? SourceReference = null);
public sealed record MrpDailyCalculation(Guid ItemId, DateOnly Date, decimal GrossDemand, decimal FirmIncoming,
    decimal FirmProjected, decimal PlanningBeforeNeed, decimal NetRequirement, decimal PlanningBalance,
    bool IsRootProduct, string Completeness);
public sealed record MrpTraceCalculation(DateOnly Date, Guid ParentItemId, Guid ChildItemId,
    Guid? BomRevisionId, Guid? BomLineId, decimal ParentNetRequirement, decimal BaseRequirement,
    decimal ScrapPercent, string ScrapSource, decimal ChildGrossRequirement);
public sealed record MrpSummaryCalculation(Guid ItemId, decimal StartingOnHand, decimal TotalGrossDemand,
    decimal TotalFirmIncoming, decimal TotalNetRequirement, decimal OverdueFirmQty, decimal MissingEtaFirmQty,
    DateOnly? StockoutDate, DateOnly? SafetyBreachDate, string? StockCoverageState,
    DateOnly? StockCoverageDate, int? StockCoverageDays, string? FirmCoverageState,
    DateOnly? FirmCoverageDate, int? FirmCoverageDays, string Completeness);
public sealed record MrpCalculationInput(DateOnly AsOfDate, DateOnly HorizonEndDate,
    IReadOnlyList<MrpItemSnapshot> Items, IReadOnlyList<MrpProductDemand> ProductDemands,
    IReadOnlyList<MrpBomEdge> BomEdges, IReadOnlyList<MrpInventoryPosition> Inventory,
    IReadOnlyList<MrpFirmSupply> FirmSupply, IReadOnlyList<MrpRouteAvailability> RouteAvailability,
    IReadOnlyList<MrpRouteChoice> RouteDecisions, IReadOnlyList<MrpCalculationException> InitialExceptions);
public sealed record MrpCalculationOutput(IReadOnlyList<MrpDailyCalculation> Daily,
    IReadOnlyList<MrpSummaryCalculation> Summaries, IReadOnlyList<MrpCalculationException> Exceptions,
    IReadOnlyList<MrpTraceCalculation> Traces, IReadOnlyList<MrpRouteChoice> EffectiveRouteDecisions,
    string Completeness);

public sealed class MrpCalculator
{
    private const decimal Scale = 1_000_000_000_000m;

    public static IReadOnlyList<(DateOnly Date, decimal Quantity)> Distribute(
        decimal quantity, DateOnly firstDate, DateOnly lastDate)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (lastDate < firstDate) return [];
        var count = lastDate.DayNumber - firstDate.DayNumber + 1;
        if (quantity == 0) return Enumerable.Range(0, count)
            .Select(offset => (firstDate.AddDays(offset), 0m)).ToList();
        var share = decimal.Truncate(quantity / count * Scale) / Scale;
        var result = new List<(DateOnly, decimal)>(count);
        decimal assigned = 0;
        for (var index = 0; index < count; index++)
        {
            var value = index == count - 1 ? quantity - assigned : share;
            result.Add((firstDate.AddDays(index), value));
            assigned += value;
        }
        return result;
    }

    public MrpCalculationOutput Calculate(MrpCalculationInput input)
    {
        if (input.HorizonEndDate < input.AsOfDate) throw new ArgumentException("MRP_HORIZON_INVALID");
        var items = input.Items.ToDictionary(x => x.ItemId);
        var exceptions = input.InitialExceptions.ToList();
        var traces = new List<MrpTraceCalculation>();
        var daily = new List<MrpDailyCalculation>();
        var effectiveChoices = input.RouteDecisions.ToDictionary(x => x.ItemId);
        var starting = input.Inventory.GroupBy(x => x.ItemId).ToDictionary(x => x.Key, x => x.Sum(y => y.OnHandQty));
        var usableAsOfFirm = input.FirmSupply.Where(x => x.OpenQty > 0 && x.Eta == input.AsOfDate && x.EtaStatus != "OVERDUE")
            .GroupBy(x => x.ItemId).ToDictionary(x => x.Key, x => x.Sum(y => y.OpenQty));
        var firmBalance = items.Keys.ToDictionary(x => x, x => starting.GetValueOrDefault(x) + usableAsOfFirm.GetValueOrDefault(x));
        var planningBalance = items.Keys.ToDictionary(x => x, x => starting.GetValueOrDefault(x) + usableAsOfFirm.GetValueOrDefault(x));
        var firmByDate = input.FirmSupply.Where(x => x.OpenQty > 0 && x.Eta.HasValue &&
                x.Eta.Value >= input.AsOfDate && x.Eta.Value <= input.HorizonEndDate && x.EtaStatus != "OVERDUE")
            .GroupBy(x => (x.ItemId, x.Eta!.Value)).ToDictionary(x => x.Key, x => x.Sum(y => y.OpenQty));
        var availability = input.RouteAvailability.ToDictionary(x => (x.ItemId, x.Date));
        var rootDemand = input.ProductDemands.Where(x => x.Date > input.AsOfDate && x.Date <= input.HorizonEndDate)
            .GroupBy(x => (x.ProductId, x.Date)).ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));

        foreach (var stock in input.Inventory.Where(x => !x.WarehouseIsActive && x.OnHandQty != 0))
            AddException(exceptions, new(stock.ItemId, null, "INACTIVE_WAREHOUSE_STOCK_INCLUDED",
                MrpExceptionSeverities.Warning, false,
                "Actual stock in an inactive warehouse remains included in the company pool.", stock.WarehouseCode));
        foreach (var supply in input.FirmSupply.Where(x => x.OpenQty > 0 && x.EtaStatus == "MISSING"))
            AddException(exceptions, new(supply.ItemId, null, "FIRM_SUPPLY_MISSING_ETA",
                MrpExceptionSeverities.Warning, false, "Firm supply has no usable ETA.", supply.PurchaseOrderNumber));
        foreach (var supply in input.FirmSupply.Where(x => x.OpenQty > 0 && x.EtaStatus == "OVERDUE"))
            AddException(exceptions, new(supply.ItemId, supply.Eta, "OVERDUE_FIRM_SUPPLY",
                MrpExceptionSeverities.Warning, false, "Overdue firm supply is excluded from timed projection.", supply.PurchaseOrderNumber));

        var eventDates = rootDemand.Keys.Select(x => x.Date)
            .Concat(firmByDate.Keys.Select(x => x.Value).Where(x => x > input.AsOfDate))
            .Distinct().Order().ToList();
        foreach (var date in eventDates)
        {
            var gross = rootDemand.Where(x => x.Key.Date == date)
                .ToDictionary(x => x.Key.ProductId, x => x.Value);
            var rootsToday = gross.Keys.ToHashSet();
            var graph = BuildGraph(date, rootsToday, items, input.BomEdges, availability, effectiveChoices, exceptions);
            var order = TopologicalOrder(graph, rootsToday, date, exceptions);

            var processingOrder = order.Concat(firmByDate.Keys.Where(x => x.Value == date)
                    .Select(x => x.ItemId).Where(x => !order.Contains(x))).ToList();
            foreach (var itemId in processingOrder)
            {
                var item = items[itemId];
                var grossQty = gross.GetValueOrDefault(itemId);
                var incoming = firmByDate.GetValueOrDefault((itemId, date));
                if (item.ItemType == ItemTypes.Product && rootsToday.Contains(itemId))
                {
                    daily.Add(new(itemId, date, grossQty, 0, 0, 0, grossQty, 0, true,
                        HasDemandError(exceptions, itemId, date) ? MrpCompleteness.Incomplete : MrpCompleteness.Complete));
                    Explode(itemId, grossQty, date, graph, items, gross, traces, exceptions);
                    continue;
                }

                if (grossQty == 0 && incoming == 0) continue;
                var beforeFirm = firmBalance.GetValueOrDefault(itemId) + incoming - grossQty;
                var beforePlanning = planningBalance.GetValueOrDefault(itemId) + incoming - grossQty;
                var net = Math.Max(item.SafetyStockQty - beforePlanning, 0m);
                var afterPlanning = beforePlanning + net;
                firmBalance[itemId] = beforeFirm;
                planningBalance[itemId] = afterPlanning;
                var incomplete = HasDemandError(exceptions, itemId, date);
                daily.Add(new(itemId, date, grossQty, incoming, beforeFirm, beforePlanning, net, afterPlanning,
                    false, incomplete ? MrpCompleteness.Incomplete : MrpCompleteness.Complete));

                var hasBomRoute = graph.ContainsKey(itemId) || item.SupplyMethod == SupplyMethods.Outsource &&
                    input.BomEdges.Any(edge => edge.ParentItemId == itemId && edge.EffectiveFrom <= date &&
                        (!edge.EffectiveTo.HasValue || edge.EffectiveTo.Value >= date));
                var route = ResolveRoute(item, date, hasBomRoute, availability, effectiveChoices, exceptions);
                if (route == MrpRoutes.External && net > 0 &&
                    (!availability.TryGetValue((itemId, date), out var ready) || !ready.ExternalReady))
                    AddException(exceptions, new(itemId, date, "NO_PLANNING_READY_EXTERNAL_SOURCE",
                        MrpExceptionSeverities.Warning, false,
                        "No planning-ready external source exists; computed net requirement is unchanged."));
                if (route == MrpRoutes.Bom && net > 0)
                    Explode(itemId, net, date, graph, items, gross, traces, exceptions);
            }
        }

        var summaries = BuildSummaries(input, items, daily, exceptions);
        var completeness = exceptions.Any(x => x.Severity == MrpExceptionSeverities.Error || x.AffectsDemand)
            ? MrpCompleteness.Incomplete
            : exceptions.Count > 0 ? MrpCompleteness.CompleteWithWarnings : MrpCompleteness.Complete;
        return new(daily, summaries, exceptions, traces, effectiveChoices.Values.OrderBy(x => x.ItemId).ToList(), completeness);
    }

    private static Dictionary<Guid, IReadOnlyList<MrpBomEdge>> BuildGraph(DateOnly date, IEnumerable<Guid> roots,
        IReadOnlyDictionary<Guid, MrpItemSnapshot> items, IReadOnlyList<MrpBomEdge> allEdges,
        IReadOnlyDictionary<(Guid, DateOnly), MrpRouteAvailability> availability,
        IDictionary<Guid, MrpRouteChoice> choices, List<MrpCalculationException> exceptions)
    {
        var graph = new Dictionary<Guid, IReadOnlyList<MrpBomEdge>>();
        var visiting = new HashSet<Guid>();
        foreach (var root in roots) Visit(root);
        return graph;

        void Visit(Guid itemId)
        {
            if (!visiting.Add(itemId) || !items.TryGetValue(itemId, out var item)) return;
            var effective = allEdges.Where(x => x.ParentItemId == item.ItemId && x.EffectiveFrom <= date &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= date)).ToList();
            var revisions = effective.Select(x => x.RevisionId).Distinct().ToList();
            if (revisions.Count > 1)
            {
                AddException(exceptions, new(item.ItemId, date, "AMBIGUOUS_EFFECTIVE_BOM",
                    MrpExceptionSeverities.Error, true, "Multiple effective BOM revisions were found."));
                return;
            }
            var hasBom = effective.Count > 0;
            var route = ResolveRoute(item, date, hasBom, availability, choices, exceptions);
            if (item.ItemType == ItemTypes.Product || route == MrpRoutes.Bom)
            {
                if (!hasBom)
                {
                    if (item.ItemType == ItemTypes.Product || item.SupplyMethod == SupplyMethods.Make)
                        AddException(exceptions, new(item.ItemId, date, "MISSING_EFFECTIVE_BOM",
                            MrpExceptionSeverities.Error, true, "No effective approved BOM exists."));
                    return;
                }
                var valid = new List<MrpBomEdge>();
                foreach (var edge in effective)
                {
                    if (!items.TryGetValue(edge.ChildItemId, out var child) || !item.IsActive || !child.IsActive)
                    {
                        AddException(exceptions, new(item.ItemId, date, "INACTIVE_BOM_ITEM",
                            MrpExceptionSeverities.Error, true, "BOM parent or child is inactive.", edge.RevisionCode));
                        continue;
                    }
                    if (item.BaseUnitId != edge.ParentBaseUnitId || child.BaseUnitId != edge.ChildBaseUnitId)
                    {
                        AddException(exceptions, new(item.ItemId, date, "BASE_UNIT_CHANGED",
                            MrpExceptionSeverities.Error, true, "BOM base-unit snapshot no longer matches Item master.", edge.RevisionCode));
                        continue;
                    }
                    valid.Add(edge);
                }
                if (valid.Count > 0)
                {
                    graph[item.ItemId] = valid;
                    foreach (var child in valid.Select(x => x.ChildItemId).Distinct()) Visit(child);
                }
            }
        }
    }

    private static string ResolveRoute(MrpItemSnapshot item, DateOnly date, bool hasBom,
        IReadOnlyDictionary<(Guid, DateOnly), MrpRouteAvailability> availability,
        IDictionary<Guid, MrpRouteChoice> choices, List<MrpCalculationException> exceptions)
    {
        if (item.ItemType == ItemTypes.Product) return MrpRoutes.Bom;
        if (item.SupplyMethod == SupplyMethods.Buy) return MrpRoutes.External;
        if (item.SupplyMethod == SupplyMethods.Make) return MrpRoutes.Bom;
        if (item.SupplyMethod == SupplyMethods.Outsource) return hasBom ? MrpRoutes.Bom : MrpRoutes.External;
        if (item.SupplyMethod != SupplyMethods.Hybrid) return MrpRoutes.External;

        var state = availability.GetValueOrDefault((item.ItemId, date));
        var bomReady = state?.BomReady == true && hasBom;
        var externalReady = state?.ExternalReady == true;
        if (choices.TryGetValue(item.ItemId, out var selected))
        {
            var valid = selected.Route == MrpRoutes.Bom ? bomReady : externalReady;
            if (!valid)
                AddException(exceptions, new(item.ItemId, date, "HYBRID_ROUTE_NOT_VALID_FOR_ALL_DATES",
                    MrpExceptionSeverities.Error, true,
                    "The Item+Run HYBRID route does not remain valid for every affected demand date."));
            return valid ? selected.Route : string.Empty;
        }
        if (bomReady && externalReady)
        {
            AddException(exceptions, new(item.ItemId, date, "HYBRID_ROUTE_DECISION_REQUIRED",
                MrpExceptionSeverities.Error, true, "Both HYBRID routes are ready and an Item+Run decision is required."));
            return string.Empty;
        }
        if (bomReady || externalReady)
        {
            var route = bomReady ? MrpRoutes.Bom : MrpRoutes.External;
            choices[item.ItemId] = new(item.ItemId, route, "AUTO");
            return route;
        }
        AddException(exceptions, new(item.ItemId, date, "HYBRID_NO_VALID_ROUTE",
            MrpExceptionSeverities.Error, true, "No HYBRID route is ready for this demand date."));
        return string.Empty;
    }

    private static IReadOnlyList<Guid> TopologicalOrder(Dictionary<Guid, IReadOnlyList<MrpBomEdge>> graph,
        IEnumerable<Guid> roots, DateOnly date, List<MrpCalculationException> exceptions)
    {
        var nodes = roots.Concat(graph.Keys).Concat(graph.Values.SelectMany(x => x).Select(x => x.ChildItemId)).ToHashSet();
        var indegree = nodes.ToDictionary(x => x, _ => 0);
        foreach (var edge in graph.Values.SelectMany(x => x)) indegree[edge.ChildItemId]++;
        var queue = new PriorityQueue<Guid, Guid>();
        foreach (var node in nodes.Where(x => indegree[x] == 0)) queue.Enqueue(node, node);
        var result = new List<Guid>(nodes.Count);
        while (queue.TryDequeue(out var node, out _))
        {
            result.Add(node);
            if (!graph.TryGetValue(node, out var edges)) continue;
            foreach (var child in edges.Select(x => x.ChildItemId).Distinct())
                if (--indegree[child] == 0) queue.Enqueue(child, child);
        }
        if (result.Count != nodes.Count)
        {
            foreach (var node in nodes.Where(x => indegree[x] > 0))
                AddException(exceptions, new(node, date, "BOM_CYCLE_DETECTED",
                    MrpExceptionSeverities.Error, true, "Cycle detected in effective MRP BOM graph."));
            result.AddRange(nodes.Except(result).Order());
        }
        return result;
    }

    private static void Explode(Guid parentId, decimal parentNet, DateOnly date,
        IReadOnlyDictionary<Guid, IReadOnlyList<MrpBomEdge>> graph,
        IReadOnlyDictionary<Guid, MrpItemSnapshot> items, IDictionary<Guid, decimal> gross,
        ICollection<MrpTraceCalculation> traces, ICollection<MrpCalculationException> exceptions)
    {
        if (parentNet <= 0 || !graph.TryGetValue(parentId, out var edges)) return;
        foreach (var edge in edges)
        {
            if (edge.BaseOutputQty <= 0 || edge.UsageQty <= 0) continue;
            var child = items[edge.ChildItemId];
            var baseRequirement = parentNet * edge.UsageQty / edge.BaseOutputQty;
            var scrap = edge.ScrapOverridePercent ?? child.DefaultScrapPercent;
            var source = edge.ScrapOverridePercent.HasValue ? "BOM_OVERRIDE" : "ITEM_DEFAULT";
            var childGross = baseRequirement * (1 + scrap / 100m);
            gross.TryGetValue(edge.ChildItemId, out var existingGross);
            gross[edge.ChildItemId] = existingGross + childGross;
            traces.Add(new(date, parentId, edge.ChildItemId, edge.RevisionId, edge.LineId,
                parentNet, baseRequirement, scrap, source, childGross));
        }
    }

    private static List<MrpSummaryCalculation> BuildSummaries(MrpCalculationInput input,
        IReadOnlyDictionary<Guid, MrpItemSnapshot> items, IReadOnlyList<MrpDailyCalculation> daily,
        IReadOnlyList<MrpCalculationException> exceptions)
    {
        var result = new List<MrpSummaryCalculation>();
        foreach (var item in items.Values)
        {
            var rows = daily.Where(x => x.ItemId == item.ItemId).OrderBy(x => x.Date).ToList();
            if (rows.Count == 0 && !input.FirmSupply.Any(x => x.ItemId == item.ItemId)) continue;
            var start = input.Inventory.Where(x => x.ItemId == item.ItemId).Sum(x => x.OnHandQty);
            var demandRows = rows.Where(x => x.GrossDemand > 0).ToList();
            var stockout = rows.FirstOrDefault(x => !x.IsRootProduct && x.FirmProjected < 0)?.Date;
            var safety = rows.FirstOrDefault(x => !x.IsRootProduct && x.FirmProjected < item.SafetyStockQty)?.Date;
            var incomplete = exceptions.Any(x => x.ItemId == item.ItemId && x.AffectsDemand);
            string? stockState = null; DateOnly? stockDate = null; int? stockDays = null;
            string? firmState = null; DateOnly? firmDate = null; int? firmDays = null;
            if (item.ItemType != ItemTypes.Product)
            {
                (stockState, stockDate, stockDays) = Coverage(rows, start, false, incomplete,
                    input.AsOfDate, input.HorizonEndDate);
                (firmState, firmDate, firmDays) = Coverage(rows,
                    start + usableFirmAtAsOf(input, item.ItemId), true, incomplete,
                    input.AsOfDate, input.HorizonEndDate);
            }
            var overdue = input.FirmSupply.Where(x => x.ItemId == item.ItemId && x.EtaStatus == "OVERDUE").Sum(x => x.OpenQty);
            var missing = input.FirmSupply.Where(x => x.ItemId == item.ItemId && x.EtaStatus == "MISSING").Sum(x => x.OpenQty);
            var eligibleFirm = input.FirmSupply.Where(x => x.ItemId == item.ItemId && x.OpenQty > 0 && x.Eta.HasValue &&
                x.Eta >= input.AsOfDate && x.Eta <= input.HorizonEndDate && x.EtaStatus != "OVERDUE").Sum(x => x.OpenQty);
            result.Add(new(item.ItemId, start, rows.Sum(x => x.GrossDemand), eligibleFirm,
                rows.Sum(x => x.NetRequirement), overdue, missing, stockout, safety, stockState, stockDate, stockDays,
                firmState, firmDate, firmDays, incomplete ? MrpCompleteness.Incomplete :
                    exceptions.Any(x => x.ItemId == item.ItemId) ? MrpCompleteness.CompleteWithWarnings : MrpCompleteness.Complete));
        }
        return result;

        static decimal usableFirmAtAsOf(MrpCalculationInput source, Guid itemId) => source.FirmSupply
            .Where(x => x.ItemId == itemId && x.OpenQty > 0 && x.Eta == source.AsOfDate && x.EtaStatus != "OVERDUE")
            .Sum(x => x.OpenQty);
    }

    private static (string State, DateOnly? Date, int? Days) Coverage(IReadOnlyList<MrpDailyCalculation> rows,
        decimal starting, bool includeFirm, bool incomplete, DateOnly asOf, DateOnly horizon)
    {
        if (incomplete) return (MrpCoverageStates.IncompleteData, null, null);
        if (!rows.Any(x => x.GrossDemand > 0)) return (MrpCoverageStates.NoFutureDemand, null, null);
        var balance = starting; DateOnly? lastCovered = null;
        foreach (var row in rows.OrderBy(x => x.Date))
        {
            if (includeFirm) balance += row.FirmIncoming;
            if (row.GrossDemand <= 0) continue;
            balance -= row.GrossDemand;
            if (balance < 0)
            {
                var through = lastCovered ?? asOf;
                return (MrpCoverageStates.Date, through, through.DayNumber - asOf.DayNumber);
            }
            lastCovered = row.Date;
        }
        return lastCovered == horizon || rows.Where(x => x.GrossDemand > 0).Last().Date <= horizon
            ? (MrpCoverageStates.BeyondHorizon, null, null)
            : (MrpCoverageStates.Date, lastCovered, lastCovered?.DayNumber - asOf.DayNumber);
    }

    private static bool HasDemandError(IEnumerable<MrpCalculationException> exceptions, Guid itemId, DateOnly date) =>
        exceptions.Any(x => x.AffectsDemand && x.ItemId == itemId && (!x.Date.HasValue || x.Date == date));

    private static void AddException(ICollection<MrpCalculationException> exceptions, MrpCalculationException value)
    {
        if (!exceptions.Any(x => x.ItemId == value.ItemId && x.Date == value.Date && x.Code == value.Code &&
            x.SourceReference == value.SourceReference)) exceptions.Add(value);
    }
}
