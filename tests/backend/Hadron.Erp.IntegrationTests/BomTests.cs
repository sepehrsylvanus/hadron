using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Bom.Domain;
using Hadron.Erp.Api.Modules.Catalog.Domain;
using Hadron.Erp.Api.Modules.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Hadron.Erp.IntegrationTests;

[Collection("ReferenceData")]
public sealed class BomTests : IClassFixture<FoundationApiFactory>, IAsyncLifetime
{
    private readonly FoundationApiFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _itemIds = [];
    private Guid _unit;

    public BomTests(FoundationApiFactory factory)
    { _factory = factory; _client = factory.CreateClient(); }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE engineering.bom_lines, engineering.bom_revisions RESTART IDENTITY CASCADE");
        var unit = new Unit($"BOM-{Guid.NewGuid():N}"[..20], $"BOM-{Guid.NewGuid():N}"[..20],
            "واحد BOM", "BOM unit", "u", 0);
        db.Units.Add(unit);
        await db.SaveChangesAsync();
        _unit = unit.Id;
        _factory.AuditSink.Clear();
    }

    public async Task DisposeAsync()
    {
        await using var db = await Db();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE engineering.bom_lines, engineering.bom_revisions RESTART IDENTITY CASCADE");
        if (_itemIds.Count > 0)
        {
            await db.MaterialItemProfiles.Where(x => _itemIds.Contains(x.ItemId)).ExecuteDeleteAsync();
            await db.Items.Where(x => _itemIds.Contains(x.Id)).ExecuteDeleteAsync();
        }
        await db.Units.Where(x => x.Id == _unit).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Relationships_identity_snapshots_and_concurrent_numbers_are_enforced()
    {
        var product = await AddItem(ItemTypes.Product);
        var part = await AddItem(ItemTypes.Part, SupplyMethods.Buy);
        var component = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var drafts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Create(product)));
        Assert.All(drafts, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        var rows = new List<BomDto>();
        foreach (var response in drafts) rows.Add(await Read(response));
        Assert.Equal(rows.Count, rows.Select(x => x.RevisionCode).Distinct().Count());
        Assert.All(rows, x => Assert.Matches("^BOM-[0-9]{6,}$", x.RevisionCode));
        Assert.All(rows, x => Assert.Equal(_unit, x.ParentBaseUnitSnapshot.Id));
        Assert.All(rows, x => Assert.Equal("واحد BOM", x.ParentBaseUnitSnapshot.NameFa));
        Assert.All(rows, x => Assert.Equal("BOM unit", x.ParentBaseUnitSnapshot.NameEn));

        var productDraft = rows[0];
        await using (var db = await Db())
        {
            Assert.Equal(await db.Items.Where(x => x.Id == product).Select(x => x.BusinessNumber).SingleAsync(),
                productDraft.ParentItem.Code);
        }
        Assert.Equal(HttpStatusCode.OK, (await AddLine(productDraft, part, 1)).StatusCode);
        productDraft = await Get(productDraft.Id);
        await using (var db = await Db())
        {
            Assert.Equal(await db.Items.Where(x => x.Id == part).Select(x => x.BusinessNumber).SingleAsync(),
                Assert.Single(productDraft.Lines).ChildItem.Code);
        }
        Assert.Equal("BOM unit", Assert.Single(productDraft.Lines).ChildBaseUnitSnapshot.NameEn);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddLine(await Read(await Create(product)), component, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AddLine(await Read(await Create(part)), component, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AddLine(await Read(await Create(component)), await AddItem(ItemTypes.Component, SupplyMethods.Buy), 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddLine(await Read(await Create(part)), part, 1)).StatusCode);
    }

    [Fact]
    public async Task Draft_validation_preserves_decimal_scrap_null_zero_and_rejects_invalid_lines()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var first = await AddItem(ItemTypes.Component, SupplyMethods.Buy, 7.25m);
        var second = await AddItem(ItemTypes.Component, SupplyMethods.Buy, 9m);
        var bom = await Read(await Create(parent));
        bom = await Read(await AddLine(bom, first, 1.234567m, null));
        bom = await Read(await AddLine(bom, second, 2m, 0m));
        Assert.Equal(1.234567m, bom.Lines[0].UsageQty);
        Assert.Null(bom.Lines[0].ScrapOverridePercent);
        Assert.Equal(7.25m, bom.Lines[0].EffectiveScrapPercent);
        Assert.Equal(0m, bom.Lines[1].ScrapOverridePercent);
        Assert.Equal(0m, bom.Lines[1].EffectiveScrapPercent);
        Assert.Equal(HttpStatusCode.Conflict, (await AddLine(bom, first, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddLine(bom, await AddItem(ItemTypes.Component, SupplyMethods.Buy), 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AddLine(bom, await AddItem(ItemTypes.Component, SupplyMethods.Buy), 1, 100.01m)).StatusCode);
        Assert.DoesNotContain(typeof(BomRevision).GetProperties(), x => x.Name.Contains("ComponentCount") || x.Name.Contains("Current"));
        Assert.DoesNotContain(typeof(BomLine).GetProperties(), x => x.Name.Contains("Requirement") || x.Name.Contains("Calculated"));
    }

    [Fact]
    public async Task Empty_draft_is_allowed_but_approval_requires_lines_and_released_records_are_immutable()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Buy);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var empty = await Read(await Create(parent, "2026-01-01"));
        var blocked = await Approve(empty);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("EMPTY_BOM", await Code(blocked));
        var bom = await Read(await AddLine(empty, child, 1));
        bom = await Read(await Approve(bom));
        Assert.Equal(BomStatuses.Approved, bom.Status);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "BomRevision" && x.Action == "APPROVE");
        var edit = await _client.PutAsJsonAsync($"/api/bom-revisions/{bom.Id}", new
        { baseOutputQty = 2, effectiveFrom = "2026-01-01", effectiveTo = (string?)null, revisionNotes = "x", expectedVersion = bom.Version });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("APPROVED_IMMUTABLE", await Code(edit));
        await using var db = await Db();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_lines SET \"UsageQty\" = 9 WHERE \"BomRevisionId\" = {bom.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Successor_closure_is_explicit_atomic_versioned_and_resolves_by_business_date()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var previous = await Approved(parent, child, "2026-01-01");
        var successor = await Read(await AddLine(await Read(await Create(parent, "2026-07-01")), child, 2));
        var noConfirmation = await Approve(successor);
        Assert.Equal(HttpStatusCode.Conflict, noConfirmation.StatusCode);
        Assert.Equal("BOM_EFFECTIVITY_OVERLAP", await Code(noConfirmation));
        var stale = await Approve(successor, previous.Id, previous.Version + 1);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(stale));
        Assert.Null((await Get(previous.Id)).EffectiveTo);
        successor = await Read(await Approve(successor, previous.Id, previous.Version));
        var closed = await Get(previous.Id);
        Assert.Equal("2026-06-30", closed.EffectiveTo);
        Assert.Equal(BomStatuses.Approved, closed.Status);
        Assert.Equal(previous.Version + 1, closed.Version);
        Assert.Equal(previous.Id, (await Resolve(parent, "2026-06-30", false)).Id);
        Assert.Equal(successor.Id, (await Resolve(parent, "2026-07-01", false)).Id);
    }

    [Fact]
    public async Task Obsolete_requires_retire_date_and_splits_operational_from_historical_resolution()
    {
        var parent = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var bom = await Approved(parent, child, "2026-01-01");
        var response = await _client.PostAsJsonAsync($"/api/bom-revisions/{bom.Id}/obsolete", new
        { expectedVersion = bom.Version, retireFromDate = "2026-04-01", reason = "retire" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var obsolete = await Read(response);
        Assert.Equal(BomStatuses.Obsolete, obsolete.Status);
        Assert.Equal("2026-03-31", obsolete.EffectiveTo);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/bom-revisions/resolve?parentItemId={parent}&businessDate=2026-03-01&historical=false")).StatusCode);
        Assert.Equal(obsolete.Id, (await Resolve(parent, "2026-03-01", true)).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/bom-revisions/resolve?parentItemId={parent}&businessDate=2026-04-01&historical=true")).StatusCode);
    }

    [Fact]
    public async Task Draft_cycle_warns_and_effective_recursive_cycle_blocks_approval()
    {
        var a = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var b = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var first = await Approved(a, b, "2026-01-01");
        Assert.Equal(BomStatuses.Approved, first.Status);
        var second = await Read(await AddLine(await Read(await Create(b, "2026-01-01")), a, 1));
        var validation = await _client.GetFromJsonAsync<ValidationDto>($"/api/bom-revisions/{second.Id}/validation");
        Assert.Contains("BOM_CYCLE_DETECTED", validation!.WarningCodes);
        var blocked = await Approve(second);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("BOM_CYCLE_DETECTED", await Code(blocked));
        var direct = await AddLine(await Read(await Create(a)), a, 1);
        Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
    }

    [Fact]
    public async Task Concurrent_graph_approval_prevents_reciprocal_cycles_and_effectivity_overlap()
    {
        var a = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var b = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var ab = await Read(await AddLine(await Read(await Create(a, "2026-01-01")), b, 1));
        var ba = await Read(await AddLine(await Read(await Create(b, "2026-01-01")), a, 1));
        var cycleResults = await Task.WhenAll(Approve(ab), Approve(ba));
        Assert.Single(cycleResults, x => x.StatusCode == HttpStatusCode.OK);
        var cycleConflict = Assert.Single(cycleResults, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("BOM_CYCLE_DETECTED", await Code(cycleConflict));

        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var first = await Read(await AddLine(await Read(await Create(parent, "2027-01-01", "2027-12-31")), child, 1));
        var second = await Read(await AddLine(await Read(await Create(parent, "2027-06-01", "2028-01-01")), child, 2));
        var overlapResults = await Task.WhenAll(Approve(first), Approve(second));
        Assert.Single(overlapResults, x => x.StatusCode == HttpStatusCode.OK);
        var overlapConflict = Assert.Single(overlapResults, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("BOM_EFFECTIVITY_OVERLAP", await Code(overlapConflict));
    }

    [Fact]
    public async Task Aggregate_concurrency_and_best_effort_audit_are_preserved()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var draft = await Read(await Create(parent, "2026-01-01"));
        var originalVersion = draft.Version;
        draft = await Read(await AddLine(draft, child, 1));
        _factory.AuditSink.Clear();
        var stale = await _client.PutAsJsonAsync($"/api/bom-revisions/{draft.Id}/lines/{draft.Lines.Single().Id}", new
        { usageQty = 2, scrapOverridePercent = (decimal?)null, sortOrder = 0, expectedVersion = originalVersion });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(stale));
        var unchanged = await Get(draft.Id);
        Assert.Equal(draft.Version, unchanged.Version);
        Assert.Equal(1m, unchanged.Lines.Single().UsageQty);
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.Action == "EDIT_LINE");

        _factory.AuditSink.ThrowOnWrite = true;
        var committed = await Create(await AddItem(ItemTypes.Part, SupplyMethods.Buy));
        Assert.Equal(HttpStatusCode.Created, committed.StatusCode);
        var committedId = (await Read(committed)).Id;
        Assert.NotNull(await Get(committedId));
        _factory.AuditSink.Clear();
    }

    [Fact]
    public async Task Stale_header_approval_and_obsolete_are_atomic_and_emit_no_success_audit()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var draft = await Read(await Create(parent, "2026-01-01"));
        var staleVersion = draft.Version;
        draft = await Read(await AddLine(draft, child, 1));
        _factory.AuditSink.Clear();

        var staleHeader = await _client.PutAsJsonAsync($"/api/bom-revisions/{draft.Id}", new
        { baseOutputQty = 9, effectiveFrom = "2026-01-01", effectiveTo = (string?)null, revisionNotes = "stale", expectedVersion = staleVersion });
        Assert.Equal(HttpStatusCode.Conflict, staleHeader.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleHeader));
        var staleApprove = await _client.PostAsJsonAsync($"/api/bom-revisions/{draft.Id}/approve", new
        {
            expectedVersion = staleVersion,
            confirmPreviousClosure = false,
            previousRevisionId = (Guid?)null,
            expectedPreviousVersion = (long?)null,
            reason = "stale"
        });
        Assert.Equal(HttpStatusCode.Conflict, staleApprove.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleApprove));
        var unchanged = await Get(draft.Id);
        Assert.Equal(BomStatuses.Draft, unchanged.Status);
        Assert.Equal(draft.Version, unchanged.Version);
        Assert.Equal(1m, unchanged.BaseOutputQty);
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.Action is "EDIT_HEADER" or "APPROVE");

        var approved = await Read(await Approve(draft));
        _factory.AuditSink.Clear();
        var staleObsolete = await _client.PostAsJsonAsync($"/api/bom-revisions/{approved.Id}/obsolete", new
        { expectedVersion = approved.Version - 1, retireFromDate = "2026-02-01", reason = "stale" });
        Assert.Equal(HttpStatusCode.Conflict, staleObsolete.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleObsolete));
        var stillApproved = await Get(approved.Id);
        Assert.Equal(BomStatuses.Approved, stillApproved.Status);
        Assert.Equal(approved.Version, stillApproved.Version);
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.Action == "OBSOLETE");
    }

    [Fact]
    public async Task Approval_and_obsolete_race_is_serialized_without_partial_lifecycle_state()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var previous = await Approved(parent, child, "2026-01-01");
        var successor = await Read(await AddLine(await Read(await Create(parent, "2026-07-01")), child, 2));
        _factory.AuditSink.Clear();
        var approveTask = Approve(successor, previous.Id, previous.Version);
        var obsoleteTask = _client.PostAsJsonAsync($"/api/bom-revisions/{previous.Id}/obsolete", new
        { expectedVersion = previous.Version, retireFromDate = "2026-06-01", reason = "race" });
        var results = await Task.WhenAll(approveTask, obsoleteTask);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var afterPrevious = await Get(previous.Id);
        var afterSuccessor = await Get(successor.Id);
        var lifecycleEvents = _factory.AuditSink.Snapshot().Where(x => x.Action is "APPROVE" or "OBSOLETE").ToArray();
        Assert.Single(lifecycleEvents);
        if (afterSuccessor.Status == BomStatuses.Approved)
        {
            Assert.Equal(BomStatuses.Approved, afterPrevious.Status);
            Assert.Equal("2026-06-30", afterPrevious.EffectiveTo);
            Assert.Equal(previous.Version + 1, afterPrevious.Version);
            Assert.Equal(successor.Version + 1, afterSuccessor.Version);
        }
        else
        {
            Assert.Equal(BomStatuses.Draft, afterSuccessor.Status);
            Assert.Equal(successor.Version, afterSuccessor.Version);
            Assert.Equal(BomStatuses.Obsolete, afterPrevious.Status);
            Assert.Equal(previous.Version + 1, afterPrevious.Version);
        }
    }

    [Fact]
    public async Task Inactive_child_is_rejected_for_new_line_without_aggregate_mutation()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        await using (var db = await Db())
        {
            var item = await db.Items.SingleAsync(x => x.Id == child);
            item.ChangeState(false);
            await db.SaveChangesAsync();
        }
        var draft = await Read(await Create(parent));
        _factory.AuditSink.Clear();
        var response = await AddLine(draft, child, 1);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = await Get(draft.Id);
        Assert.Equal(draft.Version, unchanged.Version);
        Assert.Empty(unchanged.Lines);
        Assert.DoesNotContain(_factory.AuditSink.Snapshot(), x => x.Action == "ADD_LINE");
    }

    [Fact]
    public async Task Inclusive_adjacent_open_ended_effectivity_resolves_and_boundary_overlap_is_rejected()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var first = await Read(await AddLine(await Read(await Create(parent, "2026-01-01", "2026-06-30")), child, 1));
        first = await Read(await Approve(first));
        var adjacent = await Read(await AddLine(await Read(await Create(parent, "2026-07-01")), child, 2));
        adjacent = await Read(await Approve(adjacent));
        Assert.Equal(first.Id, (await Resolve(parent, "2026-01-01", false)).Id);
        Assert.Equal(first.Id, (await Resolve(parent, "2026-06-30", false)).Id);
        Assert.Equal(adjacent.Id, (await Resolve(parent, "2026-07-01", false)).Id);
        Assert.Equal(adjacent.Id, (await Resolve(parent, "2099-12-31", false)).Id);

        var boundaryOverlap = await Read(await AddLine(await Read(await Create(parent, "2026-06-30", "2026-06-30")), child, 3));
        var rejected = await Approve(boundaryOverlap);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("BOM_EFFECTIVITY_OVERLAP", await Code(rejected));
        Assert.Equal(BomStatuses.Draft, (await Get(boundaryOverlap.Id)).Status);
    }

    [Fact]
    public async Task Effective_date_validation_ignores_non_overlapping_historical_edges()
    {
        var a = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var b = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var historical = await Read(await AddLine(await Read(await Create(a, "2026-01-01", "2026-06-30")), b, 1));
        await Read(await Approve(historical));
        var candidate = await Read(await AddLine(await Read(await Create(b, "2026-07-01")), a, 1));
        var validation = await _client.GetFromJsonAsync<ValidationDto>($"/api/bom-revisions/{candidate.Id}/validation");
        Assert.DoesNotContain("BOM_CYCLE_DETECTED", validation!.WarningCodes);
        var approved = await Approve(candidate);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task Base_unit_drift_blocks_operational_resolution_without_rewriting_snapshot()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var bom = await Approved(parent, child, "2026-01-01");
        await using (var db = await Db())
        {
            var replacement = new Unit($"NEW-{Guid.NewGuid():N}"[..20], $"NEW-{Guid.NewGuid():N}"[..20], "واحد جدید", "New", null, 0);
            db.Units.Add(replacement);
            var item = await db.Items.SingleAsync(x => x.Id == child);
            item.Edit(item.NameFa, item.NameEn, replacement.Id, item.Notes);
            await db.SaveChangesAsync();
        }
        var operational = await _client.GetAsync($"/api/bom-revisions/resolve?parentItemId={parent}&businessDate=2026-02-01&historical=false");
        Assert.Equal(HttpStatusCode.Conflict, operational.StatusCode);
        Assert.Equal("BASE_UNIT_CHANGED", await Code(operational));
        var historical = await Resolve(parent, "2026-02-01", true);
        Assert.Equal(_unit, historical.Lines.Single().ChildBaseUnitSnapshot.Id);
        Assert.Contains("BASE_UNIT_CHANGED", historical.IntegrityReasonCodes);
        Assert.Equal(bom.Id, historical.Id);
    }

    [Fact]
    public async Task Parent_base_unit_drift_blocks_operational_resolution_without_history_rewrite()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var bom = await Approved(parent, child, "2026-01-01");
        await using (var db = await Db())
        {
            var replacement = new Unit($"PNEW-{Guid.NewGuid():N}"[..20], $"PNEW-{Guid.NewGuid():N}"[..20],
                "واحد جدید Parent", "New parent", null, 0);
            db.Units.Add(replacement);
            var item = await db.Items.SingleAsync(x => x.Id == parent);
            item.Edit(item.NameFa, item.NameEn, replacement.Id, item.Notes);
            await db.SaveChangesAsync();
        }
        var operational = await _client.GetAsync($"/api/bom-revisions/resolve?parentItemId={parent}&businessDate=2026-02-01&historical=false");
        Assert.Equal(HttpStatusCode.Conflict, operational.StatusCode);
        Assert.Equal("BASE_UNIT_CHANGED", await Code(operational));
        var historical = await Resolve(parent, "2026-02-01", true);
        Assert.Equal(_unit, historical.ParentBaseUnitSnapshot.Id);
        Assert.Contains("BASE_UNIT_CHANGED", historical.IntegrityReasonCodes);
        Assert.Equal(bom.Id, historical.Id);
    }

    [Fact]
    public async Task Copy_creates_new_draft_with_current_server_owned_unit_snapshots()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var source = await Approved(parent, child, "2026-01-01");
        Guid newUnitId;
        await using (var db = await Db())
        {
            var newUnit = new Unit($"COPY-{Guid.NewGuid():N}"[..20], $"COPY-{Guid.NewGuid():N}"[..20],
                "واحد کپی", "Copy unit", null, 0);
            db.Units.Add(newUnit);
            var parentItem = await db.Items.SingleAsync(x => x.Id == parent);
            var childItem = await db.Items.SingleAsync(x => x.Id == child);
            parentItem.Edit(parentItem.NameFa, parentItem.NameEn, newUnit.Id, parentItem.Notes);
            childItem.Edit(childItem.NameFa, childItem.NameEn, newUnit.Id, childItem.Notes);
            await db.SaveChangesAsync();
            newUnitId = newUnit.Id;
        }
        var response = await _client.PostAsJsonAsync($"/api/bom-revisions/{source.Id}/copy", new
        { effectiveFrom = "2027-01-01", effectiveTo = (string?)null, revisionNotes = "copy" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = await Read(response);
        Assert.Equal(BomStatuses.Draft, copy.Status);
        Assert.NotEqual(source.Lines.Single().Id, copy.Lines.Single().Id);
        Assert.Equal(newUnitId, copy.ParentBaseUnitSnapshot.Id);
        Assert.Equal(newUnitId, copy.Lines.Single().ChildBaseUnitSnapshot.Id);
        Assert.Equal(_unit, (await Get(source.Id)).ParentBaseUnitSnapshot.Id);
        Assert.Equal(_unit, (await Get(source.Id)).Lines.Single().ChildBaseUnitSnapshot.Id);
        var editedResponse = await _client.PutAsJsonAsync($"/api/bom-revisions/{copy.Id}/lines/{copy.Lines.Single().Id}", new
        { usageQty = 8, scrapOverridePercent = 0m, sortOrder = 4, expectedVersion = copy.Version });
        Assert.Equal(HttpStatusCode.OK, editedResponse.StatusCode);
        var editedCopy = await Read(editedResponse);
        Assert.Equal(8m, editedCopy.Lines.Single().UsageQty);
        Assert.Equal(1m, (await Get(source.Id)).Lines.Single().UsageQty);
    }

    [Fact]
    public async Task Database_blocks_forbidden_lifecycle_content_and_unit_snapshot_mutations()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var draft = await Read(await AddLine(await Read(await Create(parent, "2026-01-01")), child, 1));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"Status\" = 'OBSOLETE', \"ObsoletedAtUtc\" = CURRENT_TIMESTAMP, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {draft.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"ParentBaseUnitId\" = {Guid.NewGuid()} WHERE \"Id\" = {draft.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_lines SET \"ChildBaseUnitId\" = {Guid.NewGuid()} WHERE \"BomRevisionId\" = {draft.Id}"));

        var approvedFinite = await Read(await _client.PutAsJsonAsync($"/api/bom-revisions/{draft.Id}", new
        { baseOutputQty = 1, effectiveFrom = "2026-01-01", effectiveTo = "2026-12-31", revisionNotes = (string?)null, expectedVersion = draft.Version }));
        approvedFinite = await Read(await Approve(approvedFinite));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"BaseOutputQty\" = 5 WHERE \"Id\" = {approvedFinite.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"EffectiveTo\" = NULL, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {approvedFinite.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"EffectiveTo\" = DATE '2027-01-01', \"Version\" = \"Version\" + 1 WHERE \"Id\" = {approvedFinite.Id}"));

        var obsoleteResponse = await _client.PostAsJsonAsync($"/api/bom-revisions/{approvedFinite.Id}/obsolete", new
        { expectedVersion = approvedFinite.Version, retireFromDate = "2026-06-01", reason = "test" });
        var obsolete = await Read(obsoleteResponse);
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"RevisionNotes\" = 'rewrite' WHERE \"Id\" = {obsolete.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"EffectiveTo\" = DATE '2026-12-31', \"Version\" = \"Version\" + 1 WHERE \"Id\" = {obsolete.Id}"));
    }

    [Fact]
    public async Task Buy_bom_and_inactive_historical_items_are_preserved_without_planning_scope()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Buy);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var bom = await Approved(parent, child, "2026-01-01");
        await using (var db = await Db())
        {
            var item = await db.Items.SingleAsync(x => x.Id == child);
            item.ChangeState(false);
            await db.SaveChangesAsync();
        }
        var detail = await Get(bom.Id);
        Assert.False(detail.Lines.Single().ChildItem.IsActive);
        Assert.Equal(bom.Id, (await Resolve(parent, "2026-02-01", true)).Id);
        var properties = typeof(BomRevision).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(properties, x => x.Contains("Inventory") || x.Contains("Forecast") || x.Contains("Explosion") || x.Contains("Sourcing"));
    }

    private async Task<Guid> AddItem(string type, string? supply = null, decimal scrap = 0)
    {
        await using var db = await Db();
        var prefix = type switch { ItemTypes.Product => "PRD", ItemTypes.Part => "PRT", _ => "CMP" };
        var item = new Item(type, $"{prefix}-{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}", $"قلم {Guid.NewGuid():N}", null, _unit, null);
        db.Items.Add(item);
        if (type is ItemTypes.Part or ItemTypes.Component)
        {
            var referenceType = type == ItemTypes.Part ? "PART_CATEGORY" : "COMPONENT_CATEGORY";
            var categoryType = await db.ReferenceTypes.SingleAsync(x => x.Code == referenceType);
            var category = new ReferenceValue(categoryType.Id, $"BOM-{Guid.NewGuid():N}"[..20], $"BOM-{Guid.NewGuid():N}"[..20], "گروه BOM", "BOM", 0);
            db.ReferenceValues.Add(category);
            db.MaterialItemProfiles.Add(new(item.Id, category.Id, supply!, null, scrap, 0, 0, 0, 0));
        }
        await db.SaveChangesAsync();
        _itemIds.Add(item.Id);
        return item.Id;
    }
    private Task<HttpResponseMessage> Create(Guid parent, string? from = null, string? to = null, decimal output = 1) =>
        _client.PostAsJsonAsync("/api/bom-revisions", new { parentItemId = parent, baseOutputQty = output, effectiveFrom = from, effectiveTo = to, revisionNotes = (string?)null });
    private Task<HttpResponseMessage> AddLine(BomDto bom, Guid child, decimal usage, decimal? scrap = null) =>
        _client.PostAsJsonAsync($"/api/bom-revisions/{bom.Id}/lines", new { childItemId = child, usageQty = usage, scrapOverridePercent = scrap, sortOrder = bom.LineCount, expectedVersion = bom.Version });
    private Task<HttpResponseMessage> Approve(BomDto bom, Guid? previous = null, long? previousVersion = null) =>
        _client.PostAsJsonAsync($"/api/bom-revisions/{bom.Id}/approve", new { expectedVersion = bom.Version, confirmPreviousClosure = previous.HasValue, previousRevisionId = previous, expectedPreviousVersion = previousVersion, reason = "test" });
    private async Task<BomDto> Approved(Guid parent, Guid child, string from)
    { var bom = await Read(await Create(parent, from)); bom = await Read(await AddLine(bom, child, 1)); return await Read(await Approve(bom)); }
    private async Task<BomDto> Get(Guid id) => (await _client.GetFromJsonAsync<BomDto>($"/api/bom-revisions/{id}"))!;
    private async Task<BomDto> Resolve(Guid parent, string date, bool historical) =>
        (await _client.GetFromJsonAsync<BomDto>($"/api/bom-revisions/resolve?parentItemId={parent}&businessDate={date}&historical={historical.ToString().ToLowerInvariant()}"))!;
    private async Task AssertDatabaseCheck(Func<HadronDbContext, Task> mutation)
    {
        await using var db = await Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => mutation(db));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        await transaction.RollbackAsync();
    }
    private static async Task<BomDto> Read(HttpResponseMessage response)
    { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<BomDto>())!; }
    private static async Task<string?> Code(HttpResponseMessage response)
    { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.TryGetProperty("code", out var x) ? x.GetString() : null; }
    private async Task<HadronDbContext> Db()
    { var scope = _factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HadronDbContext>>().CreateDbContextAsync(); }

    private sealed record UnitDto(Guid Id, string Code, string NameFa, string NameEn, string? Symbol, bool IsActive);
    private sealed record ItemDto(Guid Id, string Code, string NameFa, string ItemType, bool IsActive);
    private sealed record LineDto(Guid Id, ItemDto ChildItem, UnitDto ChildBaseUnitSnapshot, decimal UsageQty,
        decimal? ScrapOverridePercent, decimal EffectiveScrapPercent, int SortOrder, string[] IntegrityReasonCodes);
    private sealed record BomDto(Guid Id, string RevisionCode, ItemDto ParentItem, UnitDto ParentBaseUnitSnapshot,
        string Status, decimal BaseOutputQty, string? EffectiveFrom, string? EffectiveTo, long Version,
        int LineCount, string[] WarningCodes, string[] IntegrityReasonCodes, LineDto[] Lines);
    private sealed record ValidationDto(bool IsValidForApproval, string[] WarningCodes, string[] BlockingCodes);
}
