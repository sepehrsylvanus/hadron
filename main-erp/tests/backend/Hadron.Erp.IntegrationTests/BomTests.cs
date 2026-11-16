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
    private readonly List<Guid> _additionalUnitIds = [];
    private Guid _unit;

    public BomTests(FoundationApiFactory factory)
    { _factory = factory; _client = factory.CreateClient(); }

    public async Task InitializeAsync()
    {
        await using var db = await Db();
        await db.Database.MigrateAsync();
        TestDatabaseSafety.EnsureSafeDatabaseName(db.Database.GetDbConnection().Database);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE engineering.bom_item_applicabilities, engineering.bom_lines, engineering.bom_revisions RESTART IDENTITY CASCADE");
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
        await db.Database.ExecuteSqlRawAsync("TRUNCATE engineering.bom_item_applicabilities, engineering.bom_lines, engineering.bom_revisions RESTART IDENTITY CASCADE");
        if (_itemIds.Count > 0)
        {
            await db.MaterialItemProfiles.Where(x => _itemIds.Contains(x.ItemId)).ExecuteDeleteAsync();
            await db.Items.Where(x => _itemIds.Contains(x.Id)).ExecuteDeleteAsync();
        }
        await db.Units.Where(x => x.Id == _unit).ExecuteDeleteAsync();
        if (_additionalUnitIds.Count > 0)
            await db.Units.Where(x => _additionalUnitIds.Contains(x.Id)).ExecuteDeleteAsync();
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
        var empty = await Read(await Create(parent));
        var blocked = await Approve(empty);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("EMPTY_BOM", await Code(blocked));
        var bom = await Read(await AddLine(empty, child, 1));
        bom = await Read(await Approve(bom));
        Assert.Equal(BomStatuses.Approved, bom.Status);
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityType == "BomRevision" && x.Action == "APPROVE");
        var edit = await _client.PutAsJsonAsync($"/api/bom-revisions/{bom.Id}", new
        { baseOutputQty = 2, revisionNotes = "x", expectedVersion = bom.Version });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("APPROVED_IMMUTABLE", await Code(edit));
        await using var db = await Db();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_lines SET \"UsageQty\" = 9 WHERE \"BomRevisionId\" = {bom.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Approving_successor_atomically_replaces_the_single_current_bom()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var previous = await Approved(parent, child);
        var successor = await Read(await AddLine(await Read(await Create(parent)), child, 2));
        _factory.AuditSink.Clear();
        successor = await Read(await Approve(successor));
        var historical = await Get(previous.Id);
        Assert.Equal(BomStatuses.Obsolete, historical.Status);
        Assert.Equal(previous.Version + 1, historical.Version);
        Assert.Equal(successor.Id, (await Current(parent)).Id);
        await using var db = await Db();
        Assert.Equal(1, await db.BomRevisions.CountAsync(x => x.ParentItemId == parent && x.Status == BomStatuses.Approved));
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityId == previous.Id && x.Action == "SUPERSEDED");
        Assert.Contains(_factory.AuditSink.Snapshot(), x => x.EntityId == successor.Id && x.Action == "APPROVE");
    }

    [Fact]
    public async Task Obsolete_removes_current_bom_and_preserves_revision_history()
    {
        var parent = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var bom = await Approved(parent, child);
        var response = await _client.PostAsJsonAsync($"/api/bom-revisions/{bom.Id}/obsolete", new
        { expectedVersion = bom.Version, reason = "retire" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var obsolete = await Read(response);
        Assert.Equal(BomStatuses.Obsolete, obsolete.Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/bom-revisions/current?parentItemId={parent}")).StatusCode);
        Assert.Equal(obsolete.Id, (await Get(obsolete.Id)).Id);
        var history = (await _client.GetFromJsonAsync<BomListDto>(
            $"/api/bom-revisions?parentItemId={parent}&page=1&pageSize=25"))!;
        Assert.Contains(history.Items, x => x.Id == obsolete.Id && x.Status == BomStatuses.Obsolete);
    }

    [Fact]
    public async Task Released_structure_can_be_copied_to_another_compatible_product_as_an_independent_draft()
    {
        var sourceProduct = await AddItem(ItemTypes.Product, nameFa: "محصول مبدأ");
        var targetProduct = await AddItem(ItemTypes.Product, nameFa: "محصول مقصد");
        var part = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var source = await Approved(sourceProduct, part);
        _factory.AuditSink.Clear();

        var response = await _client.PostAsJsonAsync($"/api/bom-revisions/{source.Id}/copy", new
        {
            revisionNotes = "variant",
            targetParentItemId = targetProduct
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copied = await Read(response);
        Assert.Equal(targetProduct, copied.ParentItem.Id);
        Assert.Equal(BomStatuses.Draft, copied.Status);
        Assert.Equal(source.Id, copied.CopiedFromRevisionId);
        Assert.Equal(source.BaseOutputQty, copied.BaseOutputQty);
        Assert.Equal(source.Lines.Single().ChildItem.Id, copied.Lines.Single().ChildItem.Id);
        Assert.Equal(source.Lines.Single().UsageQty, copied.Lines.Single().UsageQty);
        Assert.NotEqual(source.Lines.Single().Id, copied.Lines.Single().Id);
        Assert.Equal(source.Id, (await Current(sourceProduct)).Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/bom-revisions/current?parentItemId={targetProduct}")).StatusCode);
        Assert.Contains(_factory.AuditSink.Snapshot(), x =>
            x.EntityId == copied.Id && x.Action == "COPY_TO_PARENT");

        var wrongType = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var wrongTypeResponse = await _client.PostAsJsonAsync($"/api/bom-revisions/{source.Id}/copy", new
        { revisionNotes = "invalid", targetParentItemId = wrongType });
        Assert.Equal(HttpStatusCode.BadRequest, wrongTypeResponse.StatusCode);

        var otherUnit = new Unit($"COPY-{Guid.NewGuid():N}"[..20], $"COPY-{Guid.NewGuid():N}"[..20],
            "واحد متفاوت", "Different unit", "du", 0);
        await using (var db = await Db())
        {
            db.Units.Add(otherUnit);
            var target = await db.Items.SingleAsync(x => x.Id == targetProduct);
            target.Edit(target.NameFa, target.NameEn, otherUnit.Id, target.Notes);
            await db.SaveChangesAsync();
        }
        _additionalUnitIds.Add(otherUnit.Id);
        var wrongUnitResponse = await _client.PostAsJsonAsync($"/api/bom-revisions/{source.Id}/copy", new
        { revisionNotes = "invalid", targetParentItemId = targetProduct });
        Assert.Equal(HttpStatusCode.BadRequest, wrongUnitResponse.StatusCode);
    }

    [Fact]
    public async Task Draft_can_be_discarded_but_released_history_and_copy_lineage_are_protected()
    {
        var product = await AddItem(ItemTypes.Product);
        var part = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var draft = await Read(await AddLine(await Read(await Create(product)), part, 2.5m));
        _factory.AuditSink.Clear();

        var discarded = await _client.PostAsJsonAsync($"/api/bom-revisions/{draft.Id}/discard", new
        { expectedVersion = draft.Version, reason = "test cleanup" });

        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/bom-revisions/{draft.Id}")).StatusCode);
        await using (var db = await Db())
        {
            Assert.False(await db.BomRevisions.AnyAsync(x => x.Id == draft.Id));
            Assert.False(await db.BomLines.AnyAsync(x => x.BomRevisionId == draft.Id));
        }
        Assert.Contains(_factory.AuditSink.Snapshot(), x =>
            x.EntityId == draft.Id && x.Action == "DISCARD_DRAFT" && x.OldValues != null && x.NewValues == null);

        var released = await Approved(product, part);
        var releasedResponse = await _client.PostAsJsonAsync($"/api/bom-revisions/{released.Id}/discard", new
        { expectedVersion = released.Version, reason = "must fail" });
        Assert.Equal(HttpStatusCode.Conflict, releasedResponse.StatusCode);
        Assert.Equal("RELEASED_BOM_IMMUTABLE", await Code(releasedResponse));
        Assert.Equal(released.Id, (await Current(product)).Id);

        var lineageSource = await Read(await Create(product));
        var successor = await Read(await _client.PostAsJsonAsync($"/api/bom-revisions/{lineageSource.Id}/copy", new
        { revisionNotes = "successor" }));
        Assert.Equal(lineageSource.Id, successor.CopiedFromRevisionId);
        var lineageResponse = await _client.PostAsJsonAsync($"/api/bom-revisions/{lineageSource.Id}/discard", new
        { expectedVersion = lineageSource.Version, reason = "must fail" });
        Assert.Equal(HttpStatusCode.Conflict, lineageResponse.StatusCode);
        Assert.Equal("BOM_DRAFT_HAS_SUCCESSOR", await Code(lineageResponse));
        Assert.Equal(lineageSource.Id, (await Get(lineageSource.Id)).Id);
    }

    [Fact]
    public async Task Draft_cycle_warns_and_current_recursive_cycle_blocks_approval()
    {
        var a = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var b = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var first = await Approved(a, b);
        Assert.Equal(BomStatuses.Approved, first.Status);
        var second = await Read(await AddLine(await Read(await Create(b)), a, 1));
        var validation = await _client.GetFromJsonAsync<ValidationDto>($"/api/bom-revisions/{second.Id}/validation");
        Assert.Contains("BOM_CYCLE_DETECTED", validation!.WarningCodes);
        var blocked = await Approve(second);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("BOM_CYCLE_DETECTED", await Code(blocked));
        var direct = await AddLine(await Read(await Create(a)), a, 1);
        Assert.Equal(HttpStatusCode.BadRequest, direct.StatusCode);
    }

    [Fact]
    public async Task Concurrent_graph_approval_prevents_cycles_and_preserves_one_current_bom_per_parent()
    {
        var a = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var b = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var ab = await Read(await AddLine(await Read(await Create(a)), b, 1));
        var ba = await Read(await AddLine(await Read(await Create(b)), a, 1));
        var cycleResults = await Task.WhenAll(Approve(ab), Approve(ba));
        Assert.Single(cycleResults, x => x.StatusCode == HttpStatusCode.OK);
        var cycleConflict = Assert.Single(cycleResults, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("BOM_CYCLE_DETECTED", await Code(cycleConflict));

        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var first = await Read(await AddLine(await Read(await Create(parent)), child, 1));
        var second = await Read(await AddLine(await Read(await Create(parent)), child, 2));
        var replacementResults = await Task.WhenAll(Approve(first), Approve(second));
        Assert.All(replacementResults, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        await using var db = await Db();
        Assert.Equal(1, await db.BomRevisions.CountAsync(x => x.ParentItemId == parent && x.Status == BomStatuses.Approved));
        Assert.Equal(1, await db.BomRevisions.CountAsync(x => x.ParentItemId == parent && x.Status == BomStatuses.Obsolete));
    }

    [Fact]
    public async Task Aggregate_concurrency_and_best_effort_audit_are_preserved()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var draft = await Read(await Create(parent));
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
        var draft = await Read(await Create(parent));
        var staleVersion = draft.Version;
        draft = await Read(await AddLine(draft, child, 1));
        _factory.AuditSink.Clear();

        var staleHeader = await _client.PutAsJsonAsync($"/api/bom-revisions/{draft.Id}", new
        { baseOutputQty = 9, revisionNotes = "stale", expectedVersion = staleVersion });
        Assert.Equal(HttpStatusCode.Conflict, staleHeader.StatusCode);
        Assert.Equal("STALE_VERSION", await Code(staleHeader));
        var staleApprove = await _client.PostAsJsonAsync($"/api/bom-revisions/{draft.Id}/approve", new
        { expectedVersion = staleVersion, reason = "stale" });
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
        { expectedVersion = approved.Version - 1, reason = "stale" });
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
        var previous = await Approved(parent, child);
        var successor = await Read(await AddLine(await Read(await Create(parent)), child, 2));
        _factory.AuditSink.Clear();
        var approveTask = Approve(successor);
        var obsoleteTask = _client.PostAsJsonAsync($"/api/bom-revisions/{previous.Id}/obsolete", new
        { expectedVersion = previous.Version, reason = "race" });
        var results = await Task.WhenAll(approveTask, obsoleteTask);
        Assert.All(results, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(results, x => x.StatusCode == HttpStatusCode.OK);
        var afterPrevious = await Get(previous.Id);
        var afterSuccessor = await Get(successor.Id);
        Assert.Equal(BomStatuses.Obsolete, afterPrevious.Status);
        Assert.Equal(BomStatuses.Approved, afterSuccessor.Status);
        Assert.Equal(successor.Id, (await Current(parent)).Id);
        await using var db = await Db();
        Assert.Equal(1, await db.BomRevisions.CountAsync(x => x.ParentItemId == parent && x.Status == BomStatuses.Approved));
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
    public async Task Bom_contract_has_no_business_dates_and_current_lookup_returns_only_the_latest_approval()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var datedPayload = await _client.PostAsJsonAsync("/api/bom-revisions", new
        {
            parentItemId = parent,
            baseOutputQty = 1,
            effectiveFrom = "2026-01-01",
            effectiveTo = "2026-12-31",
            revisionNotes = (string?)null
        });
        var first = await Read(datedPayload);
        first = await Read(await AddLine(first, child, 1));
        first = await Read(await Approve(first));
        await using (var db = await Db())
        {
            var legacyDates = await db.BomRevisions.Where(x => x.Id == first.Id)
                .Select(x => new { x.LegacyEffectiveFrom, x.LegacyEffectiveTo }).SingleAsync();
            Assert.Null(legacyDates.LegacyEffectiveFrom);
            Assert.Null(legacyDates.LegacyEffectiveTo);
        }
        var json = await datedPayload.Content.ReadAsStringAsync();
        Assert.DoesNotContain("effectiveFrom", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("effectiveTo", json, StringComparison.OrdinalIgnoreCase);

        var successor = await Read(await AddLine(await Read(await Create(parent)), child, 2));
        successor = await Read(await Approve(successor));
        Assert.Equal(BomStatuses.Obsolete, (await Get(first.Id)).Status);
        Assert.Equal(successor.Id, (await Current(parent)).Id);
    }

    [Fact]
    public async Task Cycle_validation_uses_current_boms_and_ignores_obsolete_history()
    {
        var a = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var b = await AddItem(ItemTypes.Component, SupplyMethods.Make);
        var historical = await Read(await AddLine(await Read(await Create(a)), b, 1));
        historical = await Read(await Approve(historical));
        await Read(await _client.PostAsJsonAsync($"/api/bom-revisions/{historical.Id}/obsolete",
            new { expectedVersion = historical.Version, reason = "historical" }));
        var candidate = await Read(await AddLine(await Read(await Create(b)), a, 1));
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
        var bom = await Approved(parent, child);
        await using (var db = await Db())
        {
            var replacement = new Unit($"NEW-{Guid.NewGuid():N}"[..20], $"NEW-{Guid.NewGuid():N}"[..20], "واحد جدید", "New", null, 0);
            db.Units.Add(replacement);
            var item = await db.Items.SingleAsync(x => x.Id == child);
            item.Edit(item.NameFa, item.NameEn, replacement.Id, item.Notes);
            await db.SaveChangesAsync();
        }
        var operational = await _client.GetAsync($"/api/bom-revisions/current?parentItemId={parent}");
        Assert.Equal(HttpStatusCode.Conflict, operational.StatusCode);
        Assert.Equal("BASE_UNIT_CHANGED", await Code(operational));
        var historical = await Get(bom.Id);
        Assert.Equal(_unit, historical.Lines.Single().ChildBaseUnitSnapshot.Id);
        Assert.Contains("BASE_UNIT_CHANGED", historical.IntegrityReasonCodes);
        Assert.Equal(bom.Id, historical.Id);
    }

    [Fact]
    public async Task Parent_base_unit_drift_blocks_operational_resolution_without_history_rewrite()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var bom = await Approved(parent, child);
        await using (var db = await Db())
        {
            var replacement = new Unit($"PNEW-{Guid.NewGuid():N}"[..20], $"PNEW-{Guid.NewGuid():N}"[..20],
                "واحد جدید Parent", "New parent", null, 0);
            db.Units.Add(replacement);
            var item = await db.Items.SingleAsync(x => x.Id == parent);
            item.Edit(item.NameFa, item.NameEn, replacement.Id, item.Notes);
            await db.SaveChangesAsync();
        }
        var operational = await _client.GetAsync($"/api/bom-revisions/current?parentItemId={parent}");
        Assert.Equal(HttpStatusCode.Conflict, operational.StatusCode);
        Assert.Equal("BASE_UNIT_CHANGED", await Code(operational));
        var historical = await Get(bom.Id);
        Assert.Equal(_unit, historical.ParentBaseUnitSnapshot.Id);
        Assert.Contains("BASE_UNIT_CHANGED", historical.IntegrityReasonCodes);
        Assert.Equal(bom.Id, historical.Id);
    }

    [Fact]
    public async Task Base_unit_change_api_blocks_new_bom_drift_and_allows_restoring_the_exact_snapshot_unit()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var draft = await Read(await AddLine(await Read(await Create(parent)), child, 1));
        var replacement = new Unit($"GUARD-{Guid.NewGuid():N}"[..20], $"GUARD-{Guid.NewGuid():N}"[..20],
            "واحد جایگزین", "Replacement unit", null, 0);
        await using (var db = await Db())
        {
            db.Units.Add(replacement);
            await db.SaveChangesAsync();
        }

        try
        {
            var blocked = await ChangePartUnit(parent, replacement.Id);
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal("ITEM_BASE_UNIT_LOCKED_BY_BOM", await Code(blocked));
            Assert.Contains(draft.RevisionCode, await blocked.Content.ReadAsStringAsync());

            await using (var db = await Db())
            {
                var item = await db.Items.SingleAsync(x => x.Id == parent);
                item.Edit(item.NameFa, item.NameEn, replacement.Id, item.Notes);
                await db.SaveChangesAsync();
            }

            var restored = await ChangePartUnit(parent, _unit);
            restored.EnsureSuccessStatusCode();
            await using (var db = await Db())
                Assert.Equal(_unit, await db.Items.Where(x => x.Id == parent).Select(x => x.BaseUnitId).SingleAsync());
            var validation = await _client.GetFromJsonAsync<ValidationDto>(
                $"/api/bom-revisions/{draft.Id}/validation");
            Assert.True(validation!.IsValidForApproval);
            Assert.DoesNotContain("BASE_UNIT_CHANGED", validation.BlockingCodes);
        }
        finally
        {
            await using var db = await Db();
            var item = await db.Items.SingleAsync(x => x.Id == parent);
            if (item.BaseUnitId == replacement.Id)
            {
                item.Edit(item.NameFa, item.NameEn, _unit, item.Notes);
                await db.SaveChangesAsync();
            }
            await db.Units.Where(x => x.Id == replacement.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Obsolete_bom_history_allows_a_controlled_unit_correction_and_current_successor()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var historical = await Approved(parent, child);
        var replacement = new Unit($"CORRECT-{Guid.NewGuid():N}"[..20],
            $"CORRECT-{Guid.NewGuid():N}"[..20], "واحد اصلاحی", "Corrected unit", null, 0);
        await using (var db = await Db())
        {
            db.Units.Add(replacement);
            await db.SaveChangesAsync();
            _additionalUnitIds.Add(replacement.Id);
        }

        var obsoleteResponse = await _client.PostAsJsonAsync(
            $"/api/bom-revisions/{historical.Id}/obsolete",
            new { expectedVersion = historical.Version, reason = "correct parent unit" });
        var obsolete = await Read(obsoleteResponse);
        Assert.Equal(BomStatuses.Obsolete, obsolete.Status);

        var correctedUnit = await ChangePartUnit(parent, replacement.Id);
        correctedUnit.EnsureSuccessStatusCode();
        var historicalAfterCorrection = await Get(historical.Id);
        Assert.Equal(_unit, historicalAfterCorrection.ParentBaseUnitSnapshot.Id);
        Assert.Contains("BASE_UNIT_CHANGED", historicalAfterCorrection.IntegrityReasonCodes);

        var successor = await Read(await _client.PostAsJsonAsync(
            $"/api/bom-revisions/{historical.Id}/copy",
            new { revisionNotes = "correct parent unit" }));
        Assert.Equal(replacement.Id, successor.ParentBaseUnitSnapshot.Id);
        Assert.Equal(_unit, Assert.Single(successor.Lines).ChildBaseUnitSnapshot.Id);

        successor = await Read(await Approve(successor));
        Assert.Equal(BomStatuses.Approved, successor.Status);
        Assert.Equal(successor.Id, (await Current(parent)).Id);
        Assert.DoesNotContain("BASE_UNIT_CHANGED", successor.IntegrityReasonCodes);
    }

    [Fact]
    public async Task Copy_creates_new_draft_with_current_server_owned_unit_snapshots()
    {
        var parent = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var child = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var source = await Approved(parent, child);
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
        { revisionNotes = "copy" });
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
        var draft = await Read(await AddLine(await Read(await Create(parent)), child, 1));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"Status\" = 'OBSOLETE', \"ObsoletedAtUtc\" = CURRENT_TIMESTAMP, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {draft.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"ParentBaseUnitId\" = {Guid.NewGuid()} WHERE \"Id\" = {draft.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_lines SET \"ChildBaseUnitId\" = {Guid.NewGuid()} WHERE \"BomRevisionId\" = {draft.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"EffectiveFrom\" = DATE '2026-01-01' WHERE \"Id\" = {draft.Id}"));

        var approved = await Read(await Approve(draft));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"BaseOutputQty\" = 5 WHERE \"Id\" = {approved.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"RevisionNotes\" = 'rewrite' WHERE \"Id\" = {approved.Id}"));
        await AssertDatabaseCheck(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"EffectiveTo\" = DATE '2027-01-01', \"Version\" = \"Version\" + 1 WHERE \"Id\" = {approved.Id}"));

        var competing = await Read(await AddLine(await Read(await Create(parent)), child, 2));
        await AssertDatabaseUnique(async db => await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE engineering.bom_revisions SET \"Status\" = 'APPROVED', \"ApprovedAtUtc\" = CURRENT_TIMESTAMP, \"Version\" = \"Version\" + 1 WHERE \"Id\" = {competing.Id}"));

        var obsoleteResponse = await _client.PostAsJsonAsync($"/api/bom-revisions/{approved.Id}/obsolete", new
        { expectedVersion = approved.Version, reason = "test" });
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
        var bom = await Approved(parent, child);
        await using (var db = await Db())
        {
            var item = await db.Items.SingleAsync(x => x.Id == child);
            item.ChangeState(false);
            await db.SaveChangesAsync();
        }
        var detail = await Get(bom.Id);
        Assert.False(detail.Lines.Single().ChildItem.IsActive);
        Assert.Equal(bom.Id, (await Get(bom.Id)).Id);
        var properties = typeof(BomRevision).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(properties, x => x.Contains("Inventory") || x.Contains("Forecast") || x.Contains("Explosion") || x.Contains("Sourcing"));
    }

    [Fact]
    public async Task Current_product_tree_and_where_used_follow_only_current_approved_boms()
    {
        var product = await AddItem(ItemTypes.Product, nameFa: "محصول نهایی", nameEn: "Final product");
        var part = await AddItem(ItemTypes.Part, SupplyMethods.Make, nameFa: "بدنه", nameEn: "Housing", netWeight: 0.014m);
        var component = await AddItem(ItemTypes.Component, SupplyMethods.Make, nameFa: "مجموعه پیچ", nameEn: "Fastener set", netWeight: 0.002m);
        var leaf = await AddItem(ItemTypes.Component, SupplyMethods.Buy, nameFa: "پیچ", nameEn: null, netWeight: 0.001m);

        var productBom = await Read(await Create(product));
        productBom = await Read(await AddLine(productBom, part, 2.5m));
        productBom = await Read(await Approve(productBom));
        var partBom = await Read(await Create(part));
        partBom = await Read(await AddLine(partBom, component, 4m, 0m));
        partBom = await Read(await Approve(partBom));
        var componentBom = await Read(await Create(component));
        componentBom = await Read(await AddLine(componentBom, leaf, 3m));
        componentBom = await Read(await Approve(componentBom));

        var draftOnlyChild = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var draft = await Read(await Create(component));
        await Read(await AddLine(draft, draftOnlyChild, 99m));
        await using (var db = await Db())
        {
            var inactiveLeaf = await db.Items.SingleAsync(x => x.Id == leaf);
            inactiveLeaf.ChangeState(false);
            await db.SaveChangesAsync();
        }

        var tree = (await _client.GetFromJsonAsync<TreeResponse>(
            $"/api/bom-revisions/product-tree?rootItemId={product}"))!;
        Assert.Equal("PRODUCT", tree.Root.Item.ItemType);
        Assert.Equal(productBom.Id, tree.Root.Revision!.Id);
        var partNode = Assert.Single(tree.Root.Children);
        Assert.Equal(part, partNode.Item.Id);
        Assert.Equal("Housing", partNode.Item.NameEn);
        Assert.Equal(0.014m, partNode.Item.NetWeightKg);
        Assert.Equal(2.5m, partNode.RelationshipFromParent!.UsageQty);
        var componentNode = Assert.Single(partNode.Children);
        Assert.Equal(component, componentNode.Item.Id);
        Assert.Equal(0m, componentNode.RelationshipFromParent!.ScrapOverridePercent);
        var leafNode = Assert.Single(componentNode.Children);
        Assert.Equal(leaf, leafNode.Item.Id);
        Assert.Null(leafNode.Item.NameEn);
        Assert.False(leafNode.Item.IsActive);
        Assert.Empty(leafNode.Children);
        Assert.DoesNotContain(draftOnlyChild, DescendantIds(tree.Root));

        var whereUsed = (await _client.GetFromJsonAsync<WhereUsedResponse>(
            $"/api/bom-revisions/where-used?itemId={leaf}"))!;
        Assert.Equal(component, Assert.Single(whereUsed.DirectParents).ParentItem.Id);
        Assert.Equal(3m, Assert.Single(whereUsed.DirectParents).Relationship.UsageQty);
        Assert.Equal(product, Assert.Single(whereUsed.FinalProducts).Id);

        var draftRoot = await AddItem(ItemTypes.Part, SupplyMethods.Make);
        var draftRootChild = await AddItem(ItemTypes.Component, SupplyMethods.Buy);
        var draftRootBom = await Read(await Create(draftRoot));
        await Read(await AddLine(draftRootBom, draftRootChild, 1m));
        var draftTree = (await _client.GetFromJsonAsync<TreeResponse>(
            $"/api/bom-revisions/product-tree?rootItemId={draftRoot}"))!;
        Assert.Null(draftTree.Root.Revision);
        Assert.Empty(draftTree.Root.Children);
    }

    [Fact]
    public async Task Applicability_guides_selection_without_replacing_actual_where_used_and_is_db_protected()
    {
        var product = await AddItem(ItemTypes.Product, nameFa: "محصول دامنه مصرف");
        var firstPart = await AddItem(ItemTypes.Part, SupplyMethods.Buy, nameFa: "قطعه مجاز");
        var secondPart = await AddItem(ItemTypes.Part, SupplyMethods.Buy, nameFa: "قطعه خارج از دامنه");
        var component = await AddItem(ItemTypes.Component, SupplyMethods.Buy, nameFa: "جزء نامعتبر برای محصول");

        var created = await _client.PostAsJsonAsync("/api/bom-revisions/applicabilities",
            new { parentItemId = product, childItemId = firstPart });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var relation = (await created.Content.ReadFromJsonAsync<ApplicabilityDto>())!;
        Assert.True(relation.IsActive);
        Assert.Equal(product, relation.ParentItem.Id);
        Assert.Equal(firstPart, relation.ChildItem.Id);

        var applicableOnly = (await _client.GetFromJsonAsync<ApplicableListDto>(
            $"/api/bom-revisions/applicable-items?parentItemId={product}&page=1&pageSize=25"))!;
        Assert.Equal(1, applicableOnly.TotalCount);
        Assert.Equal(firstPart, Assert.Single(applicableOnly.Items).Item.Id);
        Assert.True(applicableOnly.Items[0].IsApplicable);

        var allCandidates = (await _client.GetFromJsonAsync<ApplicableListDto>(
            $"/api/bom-revisions/applicable-items?parentItemId={product}&showAll=true&page=1&pageSize=25"))!;
        Assert.Contains(allCandidates.Items, x => x.Item.Id == firstPart && x.IsApplicable);
        Assert.Contains(allCandidates.Items, x => x.Item.Id == secondPart && !x.IsApplicable);
        Assert.DoesNotContain(allCandidates.Items, x => x.Item.Id == component);

        var actualBeforeBom = (await _client.GetFromJsonAsync<WhereUsedResponse>(
            $"/api/bom-revisions/where-used?itemId={firstPart}"))!;
        Assert.Empty(actualBeforeBom.DirectParents);
        Assert.Empty(actualBeforeBom.FinalProducts);

        var invalid = await _client.PostAsJsonAsync("/api/bom-revisions/applicabilities",
            new { parentItemId = product, childItemId = component });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var draft = await Read(await Create(product));
        await Read(await AddLine(draft, secondPart, 1m));
        var autoRegistered = (await _client.GetFromJsonAsync<ApplicabilityListDto>(
            $"/api/bom-revisions/applicabilities?childItemId={secondPart}&page=1&pageSize=25"))!;
        Assert.Equal(product, Assert.Single(autoRegistered.Items).ParentItem.Id);

        var deactivated = await _client.PostAsJsonAsync(
            $"/api/bom-revisions/applicabilities/{relation.Id}/deactivate",
            new { expectedVersion = relation.Version, reason = "test" });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var inactive = (await deactivated.Content.ReadFromJsonAsync<ApplicabilityDto>())!;
        Assert.False(inactive.IsActive);

        var reactivated = await _client.PostAsJsonAsync(
            $"/api/bom-revisions/applicabilities/{relation.Id}/reactivate",
            new { expectedVersion = inactive.Version, reason = "test" });
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);

        await AssertApplicabilityDatabaseGuard(async db =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM engineering.bom_item_applicabilities WHERE \"Id\" = {relation.Id}"));
        await AssertApplicabilityDatabaseGuard(async db =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE engineering.bom_item_applicabilities SET \"ChildItemId\" = {secondPart} WHERE \"Id\" = {relation.Id}"));
        await AssertApplicabilityDatabaseGuard(async db =>
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO engineering.bom_item_applicabilities (\"Id\", \"ParentItemId\", \"ChildItemId\", \"IsActive\", \"Version\", \"CreatedAtUtc\", \"UpdatedAtUtc\") VALUES ({Guid.NewGuid()}, {product}, {component}, TRUE, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"));
    }

    [Fact]
    public async Task Builder_tree_loads_the_reachable_editable_structure_and_prefers_latest_draft()
    {
        var product = await AddItem(ItemTypes.Product, nameFa: "محصول سازنده");
        var part = await AddItem(ItemTypes.Part, SupplyMethods.Make, nameFa: "برد");
        var releasedChild = await AddItem(ItemTypes.Component, SupplyMethods.Buy, nameFa: "نسخه منتشرشده");
        var draftChild = await AddItem(ItemTypes.Component, SupplyMethods.Buy, nameFa: "نسخه پیش‌نویس");

        var productDraft = await Read(await Create(product));
        productDraft = await Read(await AddLine(productDraft, part, 1m));
        var releasedPart = await Approved(part, releasedChild);
        var partDraft = await Read(await Create(part));
        partDraft = await Read(await AddLine(partDraft, draftChild, 2m, 0m));

        var tree = (await _client.GetFromJsonAsync<BuilderTreeResponse>(
            $"/api/bom-revisions/builder-tree?rootItemId={product}&rootRevisionId={productDraft.Id}"))!;

        Assert.Equal(product, tree.RootItem.Id);
        Assert.Equal(productDraft.Id, tree.Revisions.Single(x => x.ParentItem.Id == product).Id);
        var selectedPart = tree.Revisions.Single(x => x.ParentItem.Id == part);
        Assert.Equal(partDraft.Id, selectedPart.Id);
        Assert.NotEqual(releasedPart.Id, selectedPart.Id);
        var line = Assert.Single(selectedPart.Lines);
        Assert.Equal(draftChild, line.ChildItem.Id);
        Assert.Equal(2m, line.UsageQty);
        Assert.Equal(0m, line.ScrapOverridePercent);
        Assert.Contains(draftChild, tree.MissingParentItemIds);
        Assert.DoesNotContain(releasedChild, tree.MissingParentItemIds);
    }

    [Fact]
    public async Task Twenty_representative_product_trees_are_current_editable_and_date_free()
    {
        var revisionCodes = new HashSet<string>(StringComparer.Ordinal);

        for (var sample = 1; sample <= 20; sample++)
        {
            var product = await AddItem(ItemTypes.Product,
                nameFa: $"محصول نمونه {sample}", nameEn: $"Sample product {sample}");
            var part = await AddItem(ItemTypes.Part, SupplyMethods.Make,
                nameFa: $"قطعه نمونه {sample}", nameEn: $"Sample part {sample}");
            var component = await AddItem(ItemTypes.Component, SupplyMethods.Buy,
                sample % 3, nameFa: $"جزء نمونه {sample}", nameEn: $"Sample component {sample}");
            var productUsage = sample + 0.25m;
            var componentUsage = sample + 0.5m;

            var productDraft = await Read(await Create(product, sample % 4 + 1));
            productDraft = await Read(await AddLine(productDraft, part, productUsage,
                sample % 2 == 0 ? 0m : null));

            var partDraft = await Read(await Create(part));
            partDraft = await Read(await AddLine(partDraft, component, componentUsage,
                sample % 4 == 0 ? 2.5m : null));

            if (sample == 7)
            {
                var beforeMistake = partDraft;
                var wrongEdit = await _client.PutAsJsonAsync(
                    $"/api/bom-revisions/{partDraft.Id}/lines/{partDraft.Lines.Single().Id}", new
                    {
                        usageQty = 0,
                        scrapOverridePercent = partDraft.Lines.Single().ScrapOverridePercent,
                        sortOrder = 0,
                        expectedVersion = partDraft.Version
                    });
                Assert.Equal(HttpStatusCode.BadRequest, wrongEdit.StatusCode);
                var afterMistake = await Get(partDraft.Id);
                Assert.Equal(beforeMistake.Version, afterMistake.Version);
                Assert.Equal(componentUsage, afterMistake.Lines.Single().UsageQty);

                var correctedUsage = 7.75m;
                var corrected = await _client.PutAsJsonAsync(
                    $"/api/bom-revisions/{partDraft.Id}/lines/{partDraft.Lines.Single().Id}", new
                    {
                        usageQty = correctedUsage,
                        scrapOverridePercent = partDraft.Lines.Single().ScrapOverridePercent,
                        sortOrder = 0,
                        expectedVersion = partDraft.Version
                    });
                Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
                partDraft = await Read(corrected);
                componentUsage = correctedUsage;
                Assert.Equal(beforeMistake.Version + 1, partDraft.Version);
            }

            var partApproved = await Read(await Approve(partDraft));
            var productApproved = await Read(await Approve(productDraft));
            Assert.True(revisionCodes.Add(partApproved.RevisionCode));
            Assert.True(revisionCodes.Add(productApproved.RevisionCode));
            Assert.Equal(partApproved.Id, (await Current(part)).Id);
            Assert.Equal(productApproved.Id, (await Current(product)).Id);

            var tree = (await _client.GetFromJsonAsync<TreeResponse>(
                $"/api/bom-revisions/product-tree?rootItemId={product}"))!;
            var partNode = Assert.Single(tree.Root.Children);
            Assert.Equal(part, partNode.Item.Id);
            Assert.Equal(productUsage, partNode.RelationshipFromParent!.UsageQty);
            var componentNode = Assert.Single(partNode.Children);
            Assert.Equal(component, componentNode.Item.Id);
            Assert.Equal(componentUsage, componentNode.RelationshipFromParent!.UsageQty);
            Assert.Empty(componentNode.Children);
        }

        Assert.Equal(40, revisionCodes.Count);
        Assert.All(revisionCodes, code => Assert.Matches("^BOM-[0-9]{6,}$", code));
        await using var db = await Db();
        Assert.Equal(40, await db.BomRevisions.CountAsync(x => x.Status == BomStatuses.Approved));
        Assert.Equal(0, await db.BomRevisions.CountAsync(x => x.Status == BomStatuses.Draft));
        Assert.Equal(0, await db.BomRevisions.CountAsync(x => x.LegacyEffectiveFrom != null || x.LegacyEffectiveTo != null));
    }

    private async Task<Guid> AddItem(string type, string? supply = null, decimal scrap = 0,
        string? nameFa = null, string? nameEn = null, decimal? netWeight = null)
    {
        await using var db = await Db();
        var prefix = type switch { ItemTypes.Product => "PRD", ItemTypes.Part => "PRT", _ => "CMP" };
        var item = new Item(type, $"{prefix}-{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}",
            nameFa ?? $"قلم {Guid.NewGuid():N}", nameEn, _unit, null);
        db.Items.Add(item);
        if (type is ItemTypes.Part or ItemTypes.Component)
        {
            var referenceType = type == ItemTypes.Part ? "PART_CATEGORY" : "COMPONENT_CATEGORY";
            var categoryType = await db.ReferenceTypes.SingleAsync(x => x.Code == referenceType);
            var category = new ReferenceValue(categoryType.Id, $"BOM-{Guid.NewGuid():N}"[..20], $"BOM-{Guid.NewGuid():N}"[..20], "گروه BOM", "BOM", 0);
            db.ReferenceValues.Add(category);
            db.MaterialItemProfiles.Add(new(item.Id, category.Id, supply!, netWeight, scrap, 0, 0, 0, 0));
        }
        await db.SaveChangesAsync();
        _itemIds.Add(item.Id);
        return item.Id;
    }
    private Task<HttpResponseMessage> Create(Guid parent, decimal output = 1) =>
        _client.PostAsJsonAsync("/api/bom-revisions", new { parentItemId = parent, baseOutputQty = output, revisionNotes = (string?)null });
    private Task<HttpResponseMessage> AddLine(BomDto bom, Guid child, decimal usage, decimal? scrap = null) =>
        _client.PostAsJsonAsync($"/api/bom-revisions/{bom.Id}/lines", new { childItemId = child, usageQty = usage, scrapOverridePercent = scrap, sortOrder = bom.LineCount, expectedVersion = bom.Version });
    private Task<HttpResponseMessage> Approve(BomDto bom) =>
        _client.PostAsJsonAsync($"/api/bom-revisions/{bom.Id}/approve", new { expectedVersion = bom.Version, reason = "test" });
    private async Task<HttpResponseMessage> ChangePartUnit(Guid itemId, Guid unitId)
    {
        await using var db = await Db();
        var profile = await db.MaterialItemProfiles.AsNoTracking().Include(x => x.Item)
            .SingleAsync(x => x.ItemId == itemId);
        return await _client.PutAsJsonAsync($"/api/parts/{itemId}", new
        {
            nameFa = profile.Item.NameFa,
            nameEn = profile.Item.NameEn,
            categoryId = profile.CategoryId,
            baseUnitId = unitId,
            supplyMethod = profile.SupplyMethod,
            netWeightKg = profile.NetWeightKg,
            defaultPlanningScrapPercent = profile.DefaultPlanningScrapPercent,
            safetyStockQty = profile.SafetyStockQty,
            criticalDays = profile.CriticalDays,
            safeStartDays = profile.SafeStartDays,
            excessStartDays = profile.ExcessStartDays,
            notes = profile.Item.Notes,
            expectedVersion = profile.Item.Version
        });
    }
    private async Task<BomDto> Approved(Guid parent, Guid child)
    { var bom = await Read(await Create(parent)); bom = await Read(await AddLine(bom, child, 1)); return await Read(await Approve(bom)); }
    private async Task<BomDto> Get(Guid id) => (await _client.GetFromJsonAsync<BomDto>($"/api/bom-revisions/{id}"))!;
    private async Task<BomDto> Current(Guid parent) =>
        (await _client.GetFromJsonAsync<BomDto>($"/api/bom-revisions/current?parentItemId={parent}"))!;
    private async Task AssertDatabaseCheck(Func<HadronDbContext, Task> mutation)
    {
        await using var db = await Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => mutation(db));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        await transaction.RollbackAsync();
    }
    private async Task AssertApplicabilityDatabaseGuard(Func<HadronDbContext, Task> mutation)
    {
        await using var db = await Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => mutation(db));
        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        await transaction.RollbackAsync();
    }
    private async Task AssertDatabaseUnique(Func<HadronDbContext, Task> mutation)
    {
        await using var db = await Db();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => mutation(db));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
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
        string Status, decimal BaseOutputQty, Guid? CopiedFromRevisionId, long Version,
        int LineCount, string[] WarningCodes, string[] IntegrityReasonCodes, LineDto[] Lines);
    private sealed record BomListDto(BomDto[] Items, int Page, int PageSize, int TotalCount);
    private sealed record ValidationDto(bool IsValidForApproval, string[] WarningCodes, string[] BlockingCodes);
    private static IEnumerable<Guid> DescendantIds(TreeNode node) =>
        node.Children.SelectMany(child => DescendantIds(child).Prepend(child.Item.Id));
    private sealed record StructureItemDto(Guid Id, string Code, string NameFa, string? NameEn,
        string ItemType, bool IsActive, string? SupplyMethod, decimal? NetWeightKg, UnitDto CurrentBaseUnit);
    private sealed record RevisionRefDto(Guid Id, string RevisionCode, decimal BaseOutputQty);
    private sealed record RelationshipDto(decimal UsageQty, decimal? ScrapOverridePercent,
        UnitDto ChildBaseUnitSnapshot);
    private sealed record TreeNode(StructureItemDto Item, RevisionRefDto? Revision,
        RelationshipDto? RelationshipFromParent, TreeNode[] Children);
    private sealed record TreeResponse(TreeNode Root);
    private sealed record BuilderLineDto(Guid Id, StructureItemDto ChildItem,
        UnitDto ChildBaseUnitSnapshot, decimal UsageQty, decimal? ScrapOverridePercent,
        decimal EffectiveScrapPercent, int SortOrder, string[] IntegrityReasonCodes);
    private sealed record BuilderRevisionDto(Guid Id, string RevisionCode,
        StructureItemDto ParentItem, UnitDto ParentBaseUnitSnapshot, string Status,
        decimal BaseOutputQty, long Version,
        BuilderLineDto[] Lines);
    private sealed record BuilderTreeResponse(StructureItemDto RootItem,
        BuilderRevisionDto[] Revisions, Guid[] MissingParentItemIds);
    private sealed record WhereUsedParentDto(StructureItemDto ParentItem, RevisionRefDto Revision,
        RelationshipDto Relationship);
    private sealed record WhereUsedResponse(StructureItemDto Item,
        WhereUsedParentDto[] DirectParents, StructureItemDto[] FinalProducts);
    private sealed record ApplicableItemDto(StructureItemDto Item, bool IsApplicable);
    private sealed record ApplicableListDto(ApplicableItemDto[] Items, int Page, int PageSize, int TotalCount);
    private sealed record ApplicabilityDto(Guid Id, StructureItemDto ParentItem, StructureItemDto ChildItem,
        bool IsActive, long Version);
    private sealed record ApplicabilityListDto(ApplicabilityDto[] Items, int Page, int PageSize, int TotalCount);
}
