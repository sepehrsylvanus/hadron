using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.Mrp.Application;
using Hadron.Erp.Api.Modules.Mrp.Domain;

namespace Hadron.Erp.IntegrationTests;

public sealed class MrpCalculatorTests
{
    private static readonly DateOnly Day = new(2026, 8, 20);

    [Fact]
    public void Gd01_current_period_distribution_excludes_asof_and_places_exact_residual_last()
    {
        var asOf = new DateOnly(2026, 8, 19);
        var values = MrpCalculator.Distribute(10m, asOf.AddDays(1), asOf.AddDays(3));

        Assert.Equal(3, values.Count);
        Assert.DoesNotContain(values, x => x.Date == asOf);
        Assert.Equal(3.333333333333m, values[0].Quantity);
        Assert.Equal(3.333333333333m, values[1].Quantity);
        Assert.Equal(3.333333333334m, values[2].Quantity);
        Assert.Equal(10m, values.Sum(x => x.Quantity));
        Assert.Empty(MrpCalculator.Distribute(10m, asOf.AddDays(1), asOf));
    }

    [Fact]
    public void Gd02_nets_parent_before_explosion_and_aggregates_shared_x_once()
    {
        var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var x = Guid.NewGuid(); var y = Guid.NewGuid(); var z = Guid.NewGuid(); var unit = Guid.NewGuid();
        var items = new[]
        {
            Item(p1, "PRD-0001", ItemTypes.Product, null, unit), Item(p2, "PRD-0002", ItemTypes.Product, null, unit),
            Item(a, "PRT-0001", ItemTypes.Part, SupplyMethods.Make, unit),
            Item(b, "PRT-0002", ItemTypes.Part, SupplyMethods.Hybrid, unit),
            Item(x, "CMP-0001", ItemTypes.Component, SupplyMethods.Buy, unit, defaultScrap: 5),
            Item(y, "CMP-0002", ItemTypes.Component, SupplyMethods.Buy, unit),
            Item(z, "CMP-0003", ItemTypes.Component, SupplyMethods.Buy, unit),
        };
        var edges = new[]
        {
            Edge(p1, a, unit, 1, null), Edge(p2, b, unit, 1, null),
            Edge(a, x, unit, 2, 10), Edge(a, y, unit, 1, 0),
            Edge(b, x, unit, 1, null), Edge(b, z, unit, 2, 0),
        };
        var demand = new[] { Demand(p1, 10), Demand(p2, 5) };
        var inventory = new[] { Stock(a, unit, 3), Stock(b, unit, 0), Stock(x, unit, 8), Stock(y, unit, 2), Stock(z, unit, 12) };
        var availability = items.Where(i => i.ItemType != ItemTypes.Product)
            .Select(i => new MrpRouteAvailability(i.ItemId, Day, true, true)).ToList();
        var input = new MrpCalculationInput(Day.AddDays(-1), Day, items, demand, edges, inventory, [],
            availability, [new(b, MrpRoutes.Bom, "USER")], []);

        var output = new MrpCalculator().Calculate(input);

        Assert.Equal(7m, output.Daily.Single(r => r.ItemId == a).NetRequirement);
        var xRow = output.Daily.SingleOrDefault(r => r.ItemId == x);
        Assert.True(xRow is not null, $"daily={string.Join(',', output.Daily.Select(r => $"{r.ItemId}:{r.GrossDemand}:{r.NetRequirement}"))}; trace={string.Join(',', output.Traces.Select(t => $"{t.ParentItemId}>{t.ChildItemId}:{t.ChildGrossRequirement}"))}; errors={string.Join(',', output.Exceptions.Select(e => e.Code))}");
        Assert.Equal(20.65m, xRow.GrossDemand);
        Assert.Equal(12.65m, xRow.NetRequirement);
        Assert.Equal(15.40m, output.Traces.Single(t => t.ParentItemId == a && t.ChildItemId == x).ChildGrossRequirement);
        Assert.Equal(5.25m, output.Traces.Single(t => t.ParentItemId == b && t.ChildItemId == x).ChildGrossRequirement);
        Assert.Equal(0m, output.Daily.Single(r => r.ItemId == z).NetRequirement);
    }

    [Fact]
    public void Gd03_excludes_overdue_and_missing_eta_and_finds_stockout()
    {
        // Canonical GD-03 dates: 1405/05/10 = 2026-08-01 and 1405/05/14 = 2026-08-05.
        var q = Guid.NewGuid(); var unit = Guid.NewGuid(); var asOf = new DateOnly(2026, 8, 1);
        var demands = Enumerable.Range(1, 6).Select(i => new MrpProductDemand(q, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), asOf.AddDays(i), 20, 120, 0, unit)).ToList();
        var firm = new[]
        {
            Firm(q, 15, asOf.AddDays(2), "ON_TIME"), Firm(q, 10, asOf.AddDays(-1), "OVERDUE"),
            Firm(q, 8, null, "MISSING")
        };
        var input = new MrpCalculationInput(asOf, asOf.AddDays(6),
            [Item(q, "CMP-Q", ItemTypes.Component, SupplyMethods.Buy, unit)], demands, [],
            [Stock(q, unit, 50)], firm, Enumerable.Range(1, 6).Select(i =>
                new MrpRouteAvailability(q, asOf.AddDays(i), false, true)).ToList(), [], []);

        var output = new MrpCalculator().Calculate(input);
        var summary = output.Summaries.Single();

        Assert.Equal(new DateOnly(2026, 8, 5), summary.StockoutDate);
        Assert.Equal(10m, summary.OverdueFirmQty);
        Assert.Equal(8m, summary.MissingEtaFirmQty);
        Assert.Equal(-55m, output.Daily.Last().FirmProjected);
        Assert.DoesNotContain(output.Daily, x => x.FirmIncoming is 10 or 8);
    }

    [Fact]
    public void Gd08_mrp_run_projections_match_fixed_expected_values()
    {
        // Canonical GD-08 R1 As-Of: 1405/05/27 = 2026-08-18.
        var x = Guid.NewGuid(); var unit = Guid.NewGuid(); var asOf = new DateOnly(2026, 8, 18);
        var input = DirectMaterialInput(x, unit, asOf, [20m, 20m, 20m, 20m], 30, []);
        var r1 = new MrpCalculator().Calculate(input);
        Assert.Equal(new[] { 10m, -10m, -30m, -50m }, r1.Daily.Select(x => x.FirmProjected));
        Assert.Equal(asOf.AddDays(2), r1.Summaries.Single().StockoutDate);

        var r2Input = DirectMaterialInput(x, unit, asOf.AddDays(1), [20m, 20m, 20m], 10,
            [Firm(x, 50, asOf.AddDays(2), "ON_TIME")]);
        var r2 = new MrpCalculator().Calculate(r2Input);
        Assert.Equal(new[] { 40m, 20m, 0m }, r2.Daily.Select(x => x.FirmProjected));
        Assert.All(r2.Daily, row => Assert.Equal(0m, row.NetRequirement));
        Assert.Null(r2.Summaries.Single().StockoutDate);

        var r3 = new MrpCalculator().Calculate(DirectMaterialInput(x, unit, asOf.AddDays(2), [20m, 20m], 10,
            [Firm(x, 30, asOf.AddDays(3), "ON_TIME")]));
        Assert.Equal(new[] { 20m, 0m }, r3.Daily.Select(row => row.FirmProjected));
        Assert.All(r3.Daily, row => Assert.Equal(0m, row.NetRequirement));

        var r4 = new MrpCalculator().Calculate(DirectMaterialInput(x, unit, asOf.AddDays(3), [20m], 20, []));
        Assert.Equal(0m, r4.Daily.Single().FirmProjected);
        Assert.Equal(0m, r4.Daily.Single().NetRequirement);
    }

    [Fact]
    public void Gd09_historical_input_and_output_do_not_drift_after_new_source_values()
    {
        var x = Guid.NewGuid(); var unit = Guid.NewGuid(); var asOf = new DateOnly(2026, 8, 10);
        var historicalInput = DirectMaterialInput(x, unit, asOf, [20m, 20m], 30, []);
        var historicalOutput = new MrpCalculator().Calculate(historicalInput);
        var currentInput = DirectMaterialInput(x, unit, asOf, [25m, 25m], 100,
            [Firm(x, 10, asOf.AddDays(1), "ON_TIME")]);
        _ = new MrpCalculator().Calculate(currentInput);

        Assert.Equal(30m, historicalInput.Inventory.Single().OnHandQty);
        Assert.Equal(new[] { 10m, -10m }, historicalOutput.Daily.Select(x => x.FirmProjected));
        Assert.Equal(40m, historicalOutput.Summaries.Single().TotalGrossDemand);
    }

    [Fact]
    public void Root_product_ignores_finished_goods_stock_and_safety_and_normalizes_bom_output()
    {
        var product = Guid.NewGuid(); var child = Guid.NewGuid(); var unit = Guid.NewGuid();
        var input = new MrpCalculationInput(Day.AddDays(-1), Day,
            [Item(product, "PRD-ROOT", ItemTypes.Product, null, unit, safety: 500),
             Item(child, "CMP-CHILD", ItemTypes.Component, SupplyMethods.Buy, unit, defaultScrap: 25)],
            [Demand(product, 10)],
            [new(product, "BOM-ROOT", Guid.NewGuid(), product, child, unit, unit, 2, 3, 0,
                Day.AddDays(-1), null)],
            [Stock(product, unit, 100), Stock(child, unit, 0)], [],
            [new(child, Day, false, true)], [], []);

        var output = new MrpCalculator().Calculate(input);
        var root = output.Daily.Single(x => x.ItemId == product);
        var material = output.Daily.Single(x => x.ItemId == child);
        Assert.Equal(10m, root.NetRequirement);
        Assert.True(root.IsRootProduct);
        Assert.Equal(15m, material.GrossDemand);
        Assert.Equal(15m, material.NetRequirement);
        Assert.Equal(0m, output.Traces.Single().ScrapPercent);
        Assert.Null(output.Summaries.Single(x => x.ItemId == product).StockCoverageState);
    }

    [Fact]
    public void Material_balances_keep_hypothetical_need_separate_from_factual_firm_projection()
    {
        var item = Guid.NewGuid(); var unit = Guid.NewGuid();
        var input = new MrpCalculationInput(Day.AddDays(-1), Day,
            [Item(item, "CMP-BAL", ItemTypes.Component, SupplyMethods.Buy, unit, safety: 5)],
            [Demand(item, 20)], [], [Stock(item, unit, 10)], [Firm(item, 5, Day, "ON_TIME")],
            [new(item, Day, false, true)], [], []);

        var row = new MrpCalculator().Calculate(input).Daily.Single();
        Assert.Equal(-5m, row.FirmProjected);
        Assert.Equal(-5m, row.PlanningBeforeNeed);
        Assert.Equal(10m, row.NetRequirement);
        Assert.Equal(5m, row.PlanningBalance);
        Assert.Equal(5m, row.FirmIncoming);
    }

    [Fact]
    public void Firm_supply_at_asof_and_on_demand_free_dates_carries_forward_chronologically()
    {
        var item = Guid.NewGuid(); var unit = Guid.NewGuid(); var asOf = Day.AddDays(-1);
        var demands = new[]
        {
            new MrpProductDemand(item, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day, 8, 18, 0, unit),
            new MrpProductDemand(item, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day.AddDays(3), 10, 18, 0, unit),
        };
        var firm = new[]
        {
            Firm(item, 5, asOf, "ON_TIME"),
            Firm(item, 4, Day.AddDays(1), "ON_TIME"),
            Firm(item, 3, Day.AddDays(2), "ON_TIME"),
        };
        var input = new MrpCalculationInput(asOf, Day.AddDays(3),
            [Item(item, "CMP-TIME", ItemTypes.Component, SupplyMethods.Buy, unit)], demands, [], [], firm,
            Enumerable.Range(0, 4).Select(offset => new MrpRouteAvailability(item, Day.AddDays(offset), false, true)).ToList(), [], []);

        var output = new MrpCalculator().Calculate(input);

        Assert.Equal(new[] { Day, Day.AddDays(1), Day.AddDays(2), Day.AddDays(3) },
            output.Daily.Select(x => x.Date));
        Assert.Equal(new[] { -3m, 1m, 4m, -6m }, output.Daily.Select(x => x.FirmProjected));
        Assert.Equal(new[] { 0m, 4m, 3m, 0m }, output.Daily.Select(x => x.FirmIncoming));
        Assert.Equal(12m, output.Summaries.Single().TotalFirmIncoming);
    }

    [Fact]
    public void First_uncovered_future_demand_has_zero_day_coverage_and_stock_and_firm_views_are_distinct()
    {
        var item = Guid.NewGuid(); var unit = Guid.NewGuid(); var asOf = Day.AddDays(-1);
        var output = new MrpCalculator().Calculate(DirectMaterialInput(item, unit, asOf, [10m, 10m], 0,
            [Firm(item, 10, asOf, "ON_TIME")]));
        var summary = output.Summaries.Single();

        Assert.Equal(MrpCoverageStates.Date, summary.StockCoverageState);
        Assert.Equal(asOf, summary.StockCoverageDate);
        Assert.Equal(0, summary.StockCoverageDays);
        Assert.Equal(MrpCoverageStates.Date, summary.FirmCoverageState);
        Assert.Equal(Day, summary.FirmCoverageDate);
        Assert.Equal(1, summary.FirmCoverageDays);
    }

    [Fact]
    public void Broken_outsource_bom_never_silently_falls_back_to_external_supply()
    {
        var item = Guid.NewGuid(); var inactiveChild = Guid.NewGuid(); var unit = Guid.NewGuid();
        var active = Item(item, "PRT-OUT", ItemTypes.Part, SupplyMethods.Outsource, unit);
        var inactive = Item(inactiveChild, "CMP-OFF", ItemTypes.Component, SupplyMethods.Buy, unit) with { IsActive = false };
        var input = new MrpCalculationInput(Day.AddDays(-1), Day, [active, inactive], [Demand(item, 4)],
            [Edge(item, inactiveChild, unit, 1, 0)], [], [], [new(item, Day, false, true)], [], []);

        var output = new MrpCalculator().Calculate(input);
        Assert.Contains(output.Exceptions, x => x.Code == "INACTIVE_BOM_ITEM" && x.AffectsDemand);
        Assert.DoesNotContain(output.Exceptions, x => x.Code == "NO_PLANNING_READY_EXTERNAL_SOURCE");
        Assert.Empty(output.Traces);
        Assert.Equal(MrpCompleteness.Incomplete, output.Completeness);
    }

    [Fact]
    public void Hybrid_route_is_one_item_run_decision_and_independent_errors_do_not_kill_other_roots()
    {
        var invalidProduct = Guid.NewGuid(); var validProduct = Guid.NewGuid(); var hybrid = Guid.NewGuid();
        var child = Guid.NewGuid(); var unit = Guid.NewGuid(); var next = Day.AddDays(1);
        var items = new[]
        {
            Item(invalidProduct, "PRD-BAD", ItemTypes.Product, null, unit),
            Item(validProduct, "PRD-GOOD", ItemTypes.Product, null, unit),
            Item(hybrid, "PRT-HYB", ItemTypes.Part, SupplyMethods.Hybrid, unit),
            Item(child, "CMP-OK", ItemTypes.Component, SupplyMethods.Buy, unit),
        };
        var edges = new[] { Edge(validProduct, hybrid, unit, 1, 0), Edge(hybrid, child, unit, 1, 0) };
        var demands = new[] { Demand(invalidProduct, 1), Demand(validProduct, 2),
            new MrpProductDemand(validProduct, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), next, 2, 4, 0, unit) };
        var availability = new[]
        {
            new MrpRouteAvailability(hybrid, Day, true, true), new MrpRouteAvailability(hybrid, next, false, true),
            new MrpRouteAvailability(child, Day, false, true), new MrpRouteAvailability(child, next, false, true),
        };
        var input = new MrpCalculationInput(Day.AddDays(-1), next, items, demands, edges, [], [], availability,
            [new(hybrid, MrpRoutes.Bom, "USER")], []);

        var output = new MrpCalculator().Calculate(input);
        Assert.Contains(output.Exceptions, x => x.ItemId == invalidProduct && x.Code == "MISSING_EFFECTIVE_BOM");
        Assert.Contains(output.Exceptions, x => x.ItemId == hybrid && x.Code == "HYBRID_ROUTE_NOT_VALID_FOR_ALL_DATES");
        var unresolved = output.ResolvedRoutes.Single(x => x.ItemId == hybrid && x.DemandDate == next);
        Assert.Equal(MrpResolvedPlanningRoutes.Incomplete, unresolved.ResolvedPlanningRoute);
        Assert.Null(unresolved.RouteOrigin);
        Assert.Equal("HYBRID_ROUTE_NOT_VALID_FOR_ALL_DATES", unresolved.ReasonCode);
        Assert.Contains(output.Daily, x => x.ItemId == child && x.Date == Day && x.GrossDemand == 2);
    }

    [Fact]
    public void Hybrid_with_both_routes_ready_but_no_explicit_decision_has_no_selected_origin()
    {
        var product = Guid.NewGuid(); var hybrid = Guid.NewGuid(); var child = Guid.NewGuid(); var unit = Guid.NewGuid();
        var output = new MrpCalculator().Calculate(new(Day.AddDays(-1), Day,
            [Item(product, "PRD-HYB", ItemTypes.Product, null, unit),
             Item(hybrid, "PRT-HYB", ItemTypes.Part, SupplyMethods.Hybrid, unit),
             Item(child, "CMP-HYB", ItemTypes.Component, SupplyMethods.Buy, unit)],
            [Demand(product, 1)], [Edge(product, hybrid, unit, 1, 0), Edge(hybrid, child, unit, 1, 0)],
            [], [], [new(hybrid, Day, true, true), new(child, Day, false, true)], [], []));

        var unresolved = output.ResolvedRoutes.Single(x => x.ItemId == hybrid);
        Assert.Equal(MrpResolvedPlanningRoutes.Incomplete, unresolved.ResolvedPlanningRoute);
        Assert.Null(unresolved.RouteOrigin);
        Assert.Equal("HYBRID_ROUTE_DECISION_REQUIRED", unresolved.ReasonCode);
    }

    [Fact]
    public void Inactive_warehouse_stock_is_counted_with_warning_and_base_unit_drift_blocks_the_edge()
    {
        var product = Guid.NewGuid(); var child = Guid.NewGuid(); var unit = Guid.NewGuid(); var changedUnit = Guid.NewGuid();
        var output = new MrpCalculator().Calculate(new(Day.AddDays(-1), Day,
            [Item(product, "PRD-DRIFT", ItemTypes.Product, null, unit),
             Item(child, "CMP-DRIFT", ItemTypes.Component, SupplyMethods.Buy, changedUnit)],
            [Demand(product, 3), Demand(child, 1)], [Edge(product, child, unit, 1, 0)],
            [new MrpInventoryPosition(child, Guid.NewGuid(), "WH-OFF", false, changedUnit, 7)], [],
            [new MrpRouteAvailability(child, Day, false, true)], [], []));

        Assert.Contains(output.Exceptions, x => x.Code == "INACTIVE_WAREHOUSE_STOCK_INCLUDED" && !x.AffectsDemand);
        Assert.Contains(output.Exceptions, x => x.Code == "BASE_UNIT_CHANGED" && x.AffectsDemand);
        Assert.Equal(7m, output.Summaries.Single(x => x.ItemId == child).StartingOnHand);
    }

    [Fact]
    public void Effective_revision_changes_by_date_and_arbitrary_deep_bom_is_exploded_without_fixed_depth()
    {
        var unit = Guid.NewGuid(); var product = Guid.NewGuid();
        var nodes = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var items = new List<MrpItemSnapshot> { Item(product, "PRD-DEEP", ItemTypes.Product, null, unit) };
        items.AddRange(nodes.Select((id, index) => Item(id, $"CMP-D{index}", ItemTypes.Component,
            index == nodes.Length - 1 ? SupplyMethods.Buy : SupplyMethods.Make, unit)));
        var next = Day.AddDays(1);
        var edges = new List<MrpBomEdge>
        {
            new(Guid.NewGuid(), "BOM-OLD", Guid.NewGuid(), product, nodes[0], unit, unit, 1, 1, 0, Day.AddDays(-10), Day),
            new(Guid.NewGuid(), "BOM-NEW", Guid.NewGuid(), product, nodes[0], unit, unit, 1, 2, 0, next, null),
        };
        for (var index = 0; index < nodes.Length - 1; index++)
            edges.Add(new(Guid.NewGuid(), $"BOM-D{index}", Guid.NewGuid(), nodes[index], nodes[index + 1],
                unit, unit, 1, 1, 0, Day.AddDays(-10), null));
        var demands = new[] { Demand(product, 1), new MrpProductDemand(product, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), next, 1, 2, 0, unit) };
        var availability = nodes.SelectMany(id => new[] { new MrpRouteAvailability(id, Day, true, true),
            new MrpRouteAvailability(id, next, true, true) }).ToList();

        var output = new MrpCalculator().Calculate(new(Day.AddDays(-1), next, items, demands, edges, [], [],
            availability, [], []));

        Assert.Equal(1m, output.Traces.Single(x => x.ParentItemId == product && x.Date == Day).ChildGrossRequirement);
        Assert.Equal(2m, output.Traces.Single(x => x.ParentItemId == product && x.Date == next).ChildGrossRequirement);
        Assert.Contains(output.Traces, x => x.ParentItemId == nodes[3] && x.ChildItemId == nodes[4]);
    }

    [Fact]
    public void New_run_persists_every_canonical_resolved_route_origin_at_item_and_demand_date_grain()
    {
        var unit = Guid.NewGuid();
        var buy = Guid.NewGuid(); var make = Guid.NewGuid(); var outsourceExternal = Guid.NewGuid();
        var outsourceBom = Guid.NewGuid(); var hybridExternal = Guid.NewGuid(); var hybridInternal = Guid.NewGuid();
        var child = Guid.NewGuid();
        var items = new[]
        {
            Item(buy, "CMP-BUY", ItemTypes.Component, SupplyMethods.Buy, unit),
            Item(make, "CMP-MAKE", ItemTypes.Component, SupplyMethods.Make, unit),
            Item(outsourceExternal, "CMP-OE", ItemTypes.Component, SupplyMethods.Outsource, unit),
            Item(outsourceBom, "CMP-OB", ItemTypes.Component, SupplyMethods.Outsource, unit),
            Item(hybridExternal, "CMP-HE", ItemTypes.Component, SupplyMethods.Hybrid, unit),
            Item(hybridInternal, "CMP-HI", ItemTypes.Component, SupplyMethods.Hybrid, unit),
            Item(child, "CMP-CHILD", ItemTypes.Component, SupplyMethods.Buy, unit),
        };
        var demands = new[] { buy, make, outsourceExternal, outsourceBom, hybridExternal, hybridInternal }
            .Select(id => new MrpProductDemand(id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day, 1, 1, 0, unit)).ToList();
        var edges = new[]
        {
            Edge(make, child, unit, 1, 0), Edge(outsourceBom, child, unit, 1, 0),
            Edge(hybridExternal, child, unit, 1, 0), Edge(hybridInternal, child, unit, 1, 0),
        };
        var availability = items.Select(item => new MrpRouteAvailability(item.ItemId, Day,
            edges.Any(edge => edge.ParentItemId == item.ItemId), true)).ToList();
        var output = new MrpCalculator().Calculate(new(Day.AddDays(-1), Day, items, demands, edges, [], [],
            availability, [new(hybridExternal, MrpRoutes.External, "USER"),
                new(hybridInternal, MrpRoutes.Bom, "USER")], []));

        var routes = output.ResolvedRoutes.Where(x => x.ItemId != child).ToDictionary(x => x.ItemId);
        Assert.Equal((MrpResolvedPlanningRoutes.External, MrpRouteOrigins.Buy),
            (routes[buy].ResolvedPlanningRoute, routes[buy].RouteOrigin));
        Assert.Equal((MrpResolvedPlanningRoutes.InternalBom, MrpRouteOrigins.Make),
            (routes[make].ResolvedPlanningRoute, routes[make].RouteOrigin));
        Assert.Equal((MrpResolvedPlanningRoutes.External, MrpRouteOrigins.OutsourceExternal),
            (routes[outsourceExternal].ResolvedPlanningRoute, routes[outsourceExternal].RouteOrigin));
        Assert.Equal((MrpResolvedPlanningRoutes.InternalBom, MrpRouteOrigins.OutsourceBom),
            (routes[outsourceBom].ResolvedPlanningRoute, routes[outsourceBom].RouteOrigin));
        Assert.Equal((MrpResolvedPlanningRoutes.External, MrpRouteOrigins.HybridExternal),
            (routes[hybridExternal].ResolvedPlanningRoute, routes[hybridExternal].RouteOrigin));
        Assert.Equal((MrpResolvedPlanningRoutes.InternalBom, MrpRouteOrigins.HybridInternal),
            (routes[hybridInternal].ResolvedPlanningRoute, routes[hybridInternal].RouteOrigin));
        Assert.All(routes.Values, route => Assert.Equal(Day, route.DemandDate));
    }

    [Fact]
    public void Approved_manufacturing_route_moves_polymer_need_back_by_working_days()
    {
        var product = Guid.NewGuid(); var body = Guid.NewGuid(); var polymer = Guid.NewGuid();
        var unit = Guid.NewGuid(); var route = Guid.NewGuid(); var workCenter = Guid.NewGuid();
        var completion = new DateOnly(2026, 9, 10); // Thursday
        const int saturdayThroughThursday = 0b1011111;
        var output = new MrpCalculator().Calculate(new(
            new DateOnly(2026, 9, 1), completion,
            [Item(product, "PRD-ROUTE", ItemTypes.Product, null, unit),
             Item(body, "PRT-BODY", ItemTypes.Part, SupplyMethods.Make, unit),
             Item(polymer, "CMP-ABS", ItemTypes.Component, SupplyMethods.Buy, unit)],
            [new MrpProductDemand(product, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                completion, 1_000m, 1_000m, 0, unit)],
            [Edge(product, body, unit, 1, 0), Edge(body, polymer, unit, 0.014m, 0)],
            [], [], [new(polymer, completion, false, true)], [], [],
            [new(route, "RTE-0001", product, Guid.NewGuid(), 10, workCenter, "WC-0001",
                saturdayThroughThursday, 0, 1),
             new(route, "RTE-0002", body, Guid.NewGuid(), 10, workCenter, "WC-0001",
                saturdayThroughThursday, 4, 0)]));

        var bodyNeed = completion.AddDays(-1);
        var polymerNeed = new DateOnly(2026, 9, 5); // Saturday; Friday is not a working day.
        Assert.Contains(output.Daily, x => x.ItemId == body && x.Date == bodyNeed && x.GrossDemand == 1_000m);
        Assert.Contains(output.Daily, x => x.ItemId == polymer && x.Date == polymerNeed && x.GrossDemand == 14m);
        var polymerTrace = output.Traces.Single(x => x.ParentItemId == body && x.ChildItemId == polymer);
        Assert.Equal(bodyNeed, polymerTrace.ParentDemandDate);
        Assert.Equal(polymerNeed, polymerTrace.Date);
        Assert.DoesNotContain(output.Exceptions, x => x.Code == "MISSING_APPROVED_MANUFACTURING_ROUTE");
    }

    [Fact]
    public void Missing_internal_route_is_blocking_but_keeps_material_quantity_visible()
    {
        var product = Guid.NewGuid(); var material = Guid.NewGuid(); var unit = Guid.NewGuid();
        var output = new MrpCalculator().Calculate(new(Day.AddDays(-1), Day,
            [Item(product, "PRD-NO-ROUTE", ItemTypes.Product, null, unit),
             Item(material, "CMP-MATERIAL", ItemTypes.Component, SupplyMethods.Buy, unit)],
            [Demand(product, 10)], [Edge(product, material, unit, 2, 0)], [], [],
            [new(material, Day, false, true)], [], [], []));

        Assert.Contains(output.Exceptions, x => x.ItemId == product &&
            x.Code == "MISSING_APPROVED_MANUFACTURING_ROUTE" && x.AffectsDemand);
        Assert.Contains(output.Daily, x => x.ItemId == material && x.GrossDemand == 20m);
        Assert.Equal(MrpCompleteness.Incomplete, output.Completeness);
    }

    private static MrpCalculationInput DirectMaterialInput(Guid itemId, Guid unit, DateOnly asOf,
        IReadOnlyList<decimal> quantities, decimal stock, IReadOnlyList<MrpFirmSupply> firm) =>
        new(asOf, asOf.AddDays(quantities.Count),
            [Item(itemId, "CMP-X", ItemTypes.Component, SupplyMethods.Buy, unit)],
            quantities.Select((qty, i) => new MrpProductDemand(itemId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                asOf.AddDays(i + 1), qty, quantities.Sum(), 0, unit)).ToList(), [], [Stock(itemId, unit, stock)], firm,
            quantities.Select((_, i) => new MrpRouteAvailability(itemId, asOf.AddDays(i + 1), false, true)).ToList(), [], []);

    private static MrpItemSnapshot Item(Guid id, string code, string type, string? supply, Guid unit,
        decimal defaultScrap = 0, decimal safety = 0) => new(id, code, type, code, code, true, unit, supply, safety, defaultScrap);
    private static MrpProductDemand Demand(Guid product, decimal qty) =>
        new(product, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Day, qty, qty, 0, Guid.Empty);
    private static MrpBomEdge Edge(Guid parent, Guid child, Guid unit, decimal usage, decimal? scrap) =>
        new(parent, "BOM", Guid.NewGuid(), parent, child, unit, unit, 1, usage, scrap,
            Day.AddDays(-10), null);
    private static MrpInventoryPosition Stock(Guid item, Guid unit, decimal qty) =>
        new(item, Guid.NewGuid(), "WH-0001", true, unit, qty);
    private static MrpFirmSupply Firm(Guid item, decimal qty, DateOnly? eta, string status) =>
        new(item, Guid.NewGuid(), "PO-000001", Guid.NewGuid(), Guid.NewGuid(), qty, eta,
            eta.HasValue ? "ORIGINAL_PLANNED_WAREHOUSE_RECEIPT" : "MISSING", status, "ORDERED");
}
