using System.Data;
using System.Security.Cryptography;
using System.Text;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed class MrpPlanLifecycleService(
    IDbContextFactory<HadronDbContext> factory,
    TimeProvider timeProvider)
{
    private const string LockName = "hadron:planning:mrp-plan-slots:v1";

    public Task ActivateCurrentAsync(Guid runId, CancellationToken ct) =>
        ActivateAsync(MrpPlanSlotNames.Current, runId, null, ct);

    public Task CertifyAsync(Guid runId, DateOnly businessDate, CancellationToken ct) =>
        ActivateAsync(MrpPlanSlotNames.Certified, runId, businessDate, ct);

    public async Task<bool> HasCertificateAsync(DateOnly businessDate, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MrpDailyCertificates.AsNoTracking()
            .AnyAsync(x => x.BusinessDate == businessDate, ct);
    }

    public async Task<MrpPlanSlot?> CurrentSlotAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.MrpPlanSlots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SlotName == MrpPlanSlotNames.Current, ct);
    }

    public async Task DiscardTransientRunAsync(Guid runId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({LockName}, 0))", ct);
        await PruneIfTransientAndUnpinned(db, runId, ct);
        await tx.CommitAsync(ct);
    }

    private async Task ActivateAsync(string slotName, Guid runId, DateOnly? certificateDate,
        CancellationToken ct)
    {
        CertificateMaterial? certificate = null;
        if (slotName == MrpPlanSlotNames.Certified)
            certificate = await AuditAsync(runId, certificateDate!.Value, ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({LockName}, 0))", ct);

        var run = await db.MrpRuns.SingleOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new KeyNotFoundException("MRP Run was not found.");
        EnsureSuccessful(run);

        if (certificate is not null && await db.MrpDailyCertificates
                .AnyAsync(x => x.BusinessDate == certificate.BusinessDate, ct))
        {
            await tx.CommitAsync(ct);
            return;
        }

        var slot = await db.MrpPlanSlots.SingleOrDefaultAsync(x => x.SlotName == slotName, ct);
        var previousRunId = slot?.RunId;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var sourceCutoff = run.SnapshotCapturedAtUtc!.Value;
        if (slot is null)
            db.MrpPlanSlots.Add(new MrpPlanSlot(slotName, run.Id,
                certificate?.BusinessDate ?? run.AsOfDate, sourceCutoff, now));
        else
            slot.Replace(run.Id, certificate?.BusinessDate ?? run.AsOfDate, sourceCutoff, now);

        if (certificate is not null)
            db.MrpDailyCertificates.Add(new MrpDailyCertificate(certificate.BusinessDate, run.Id,
                run.RunNumber, run.AsOfDate, run.HorizonEndDate, sourceCutoff, now, run.EngineVersion,
                run.Status, run.Completeness, certificate.ItemCount, certificate.DailyResultCount,
                certificate.ExceptionCount, certificate.ResultHash));

        await db.SaveChangesAsync(ct);
        if (previousRunId.HasValue && previousRunId.Value != run.Id)
            await PruneIfTransientAndUnpinned(db, previousRunId.Value, ct);
        await tx.CommitAsync(ct);
    }

    private async Task<CertificateMaterial> AuditAsync(Guid runId, DateOnly businessDate,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var run = await db.MrpRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new KeyNotFoundException("MRP Run was not found.");
        EnsureSuccessful(run);
        if (run.AsOfDate != businessDate)
            throw new MrpConflictException("MRP_CERTIFICATE_DATE_MISMATCH",
                "The certified business date must equal the Run As-Of date.");

        var outOfRange = await db.MrpRunDailyResults.AsNoTracking().CountAsync(x =>
            x.RunId == runId && (x.Date < run.AsOfDate || x.Date > run.HorizonEndDate), ct);
        if (outOfRange != 0)
            throw new MrpConflictException("MRP_DAILY_RESULT_OUTSIDE_HORIZON",
                "Daily results outside the Run horizon cannot be certified.");

        var orphanDaily = await db.MrpRunDailyResults.AsNoTracking().CountAsync(x =>
            x.RunId == runId && !db.MrpRunItemInputs.Any(i => i.RunId == runId && i.ItemId == x.ItemId), ct);
        if (orphanDaily != 0)
            throw new MrpConflictException("MRP_DAILY_RESULT_ITEM_MISSING",
                "Daily results without a matching Item snapshot cannot be certified.");

        var itemCount = await db.MrpRunItemInputs.AsNoTracking().CountAsync(x => x.RunId == runId, ct);
        var dailyCount = await db.MrpRunDailyResults.AsNoTracking().CountAsync(x => x.RunId == runId, ct);
        var exceptionCount = await db.MrpRunExceptions.AsNoTracking().CountAsync(x => x.RunId == runId, ct);
        return new(businessDate, itemCount, dailyCount, exceptionCount,
            await CalculateHash(db, runId, ct));
    }

    private static void EnsureSuccessful(MrpRun run)
    {
        if (run.Status is not (MrpRunStatuses.Completed or MrpRunStatuses.CompletedWithExceptions) ||
            run.SnapshotCapturedAtUtc is null || run.CompletedAtUtc is null)
            throw new MrpConflictException("MRP_RUN_NOT_CERTIFIABLE",
                "Only a completed Run with a committed source snapshot can become an active plan.");
    }

    private static async Task<string> CalculateHash(HadronDbContext db, Guid runId, CancellationToken ct)
    {
        const string sql = """
            SELECT "Source", "Payload"
            FROM (
                SELECT '01-item' AS "Source", (to_jsonb(x) - 'Id' - 'RunId')::text AS "Payload" FROM planning.mrp_run_item_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '02-forecast', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_forecast_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '03-forecast-period', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_forecast_period_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '04-bom', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_bom_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '05-inventory', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_inventory_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '06-firm', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_firm_supply_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '07-route-availability', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_route_availability_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '08-external', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_external_source_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '09-manufacturing-operation', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_manufacturing_operation_inputs x WHERE x."RunId" = @run_id
                UNION ALL SELECT '10-route-decision', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_route_decisions x WHERE x."RunId" = @run_id
                UNION ALL SELECT '11-route-result', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_resolved_route_results x WHERE x."RunId" = @run_id
                UNION ALL SELECT '12-daily', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_daily_results x WHERE x."RunId" = @run_id
                UNION ALL SELECT '13-summary', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_item_summaries x WHERE x."RunId" = @run_id
                UNION ALL SELECT '14-exception', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_exceptions x WHERE x."RunId" = @run_id
                UNION ALL SELECT '15-trace', (to_jsonb(x) - 'Id' - 'RunId')::text FROM planning.mrp_run_demand_traces x WHERE x."RunId" = @run_id
            ) q
            ORDER BY "Source", "Payload"
            """;
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("run_id", runId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (await reader.ReadAsync(ct))
        {
            var line = $"{reader.GetString(0)}\t{reader.GetString(1)}\n";
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task PruneIfTransientAndUnpinned(HadronDbContext db, Guid runId,
        CancellationToken ct)
    {
        var run = await db.MrpRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run is null || run.Purpose is MrpRunPurposes.Published ||
            await db.MrpPlanSlots.AnyAsync(x => x.RunId == runId, ct) ||
            await db.OrderPlanningGenerations.AnyAsync(x => x.RunId == runId, ct) ||
            await db.PurchaseOrderLines.AnyAsync(x => x.MrpRunId == runId, ct) ||
            await db.PlannerWorkItems.AnyAsync(x => x.RunId == runId, ct) ||
            await db.WhatIfScenarios.AnyAsync(x => x.BaseRunId == runId, ct)) return;

        await db.MrpRunDemandTraces.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunExceptions.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunItemSummaries.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunDailyResults.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunResolvedRouteResults.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunRouteDecisions.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunExternalSourceInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunManufacturingOperationInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunRouteAvailabilityInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunFirmSupplyInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunInventoryInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunBomInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunForecastPeriodInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunForecastInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRunItemInputs.Where(x => x.RunId == runId).ExecuteDeleteAsync(ct);
        await db.MrpRuns.Where(x => x.Id == runId).ExecuteDeleteAsync(ct);
    }

    private sealed record CertificateMaterial(DateOnly BusinessDate, int ItemCount,
        int DailyResultCount, int ExceptionCount, string ResultHash);
}
