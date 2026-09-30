using System.Data;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Mrp.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hadron.Erp.Api.Modules.Mrp.Application;

public sealed class AutomaticMrpRefreshProcessor(
    IDbContextFactory<HadronDbContext> factory,
    MrpService mrpService,
    MrpPlanLifecycleService planLifecycle,
    IPlanningCalendar calendar,
    TimeProvider timeProvider,
    IOptions<AutomaticMrpRefreshOptions> options,
    ILogger<AutomaticMrpRefreshProcessor> logger)
{
    private const string LockName = "hadron:planning:automatic-mrp-refresh:v1";

    public async Task<bool> ProcessOnceAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return false;

        await using var lockDb = await factory.CreateDbContextAsync(ct);
        var connection = (NpgsqlConnection)lockDb.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        if (!await TryLock(connection, ct)) return false;

        List<Guid> claimedIds = [];
        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            await RecoverExpiredLeases(lockDb, now, ct);
            if (await EnsurePreviousDayCertified(lockDb, ct)) return true;
            await EnsureCalendarRefresh(lockDb, now, ct);

            var pending = await lockDb.MrpRefreshOutboxMessages
                .Where(x => x.Status == MrpRefreshOutboxStatuses.Pending && x.AvailableAtUtc <= now)
                .OrderBy(x => x.OccurredAtUtc).Take(5000).ToListAsync(ct);
            if (pending.Count == 0) return false;

            var latest = await (from slot in lockDb.MrpPlanSlots.AsNoTracking()
                                join candidateRun in lockDb.MrpRuns.AsNoTracking() on slot.RunId equals candidateRun.Id
                                where slot.SlotName == MrpPlanSlotNames.Current && candidateRun.SnapshotCapturedAtUtc != null
                                select candidateRun)
                .Select(x => new { x.Id, SnapshotAt = x.SnapshotCapturedAtUtc!.Value })
                .FirstOrDefaultAsync(ct);
            var newestChange = pending.Max(x => x.OccurredAtUtc);
            if (latest is not null && latest.SnapshotAt >= newestChange)
            {
                foreach (var message in pending) message.MarkCompleted(latest.Id, now);
                await lockDb.SaveChangesAsync(ct);
                return true;
            }

            foreach (var message in pending) message.MarkProcessing(now);
            await lockDb.SaveChangesAsync(ct);
            claimedIds = pending.Select(x => x.Id).ToList();

            var run = await mrpService.ExecuteAutomatic(calendar.TehranToday, ct);
            await Complete(claimedIds, run.Id, timeProvider.GetUtcNow().UtcDateTime, ct);
            logger.LogInformation("Automatic MRP refresh completed as {RunNumber} for {MessageCount} input changes.",
                run.RunNumber, claimedIds.Count);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (claimedIds.Count > 0)
                await Retry(claimedIds, "WORKER_CANCELLED", "Automatic MRP refresh was cancelled.",
                    CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            if (claimedIds.Count > 0)
                await Retry(claimedIds, "AUTOMATIC_MRP_REFRESH_FAILED", exception.Message,
                    CancellationToken.None);
            logger.LogError(exception, "Automatic MRP refresh failed for {MessageCount} input changes.",
                claimedIds.Count);
            return claimedIds.Count > 0;
        }
        finally
        {
            await Unlock(connection);
        }
    }

    private async Task<bool> EnsurePreviousDayCertified(HadronDbContext db, CancellationToken ct)
    {
        var businessDate = calendar.TehranToday.AddDays(-1);
        if (await db.MrpDailyCertificates.AsNoTracking()
            .AnyAsync(x => x.BusinessDate == businessDate, ct)) return false;

        var current = await (from slot in db.MrpPlanSlots.AsNoTracking()
                             join run in db.MrpRuns.AsNoTracking() on slot.RunId equals run.Id
                             where slot.SlotName == MrpPlanSlotNames.Current
                             select new { run.Id, run.AsOfDate }).SingleOrDefaultAsync(ct);
        if (current is null) return false;

        if (current.AsOfDate == businessDate)
            await planLifecycle.CertifyAsync(current.Id, businessDate, ct);
        else
            await mrpService.ExecuteCertified(businessDate, ct);
        logger.LogInformation("MRP daily certified plan completed for {BusinessDate}.", businessDate);
        return true;
    }

    private async Task EnsureCalendarRefresh(HadronDbContext db, DateTime now, CancellationToken ct)
    {
        var today = calendar.TehranToday;
        var hasWork = await db.MrpRefreshOutboxMessages.AnyAsync(x =>
            x.Status == MrpRefreshOutboxStatuses.Pending || x.Status == MrpRefreshOutboxStatuses.Processing, ct);
        if (hasWork) return;
        var latestAsOf = await (from slot in db.MrpPlanSlots.AsNoTracking()
                                join run in db.MrpRuns.AsNoTracking() on slot.RunId equals run.Id
                                where slot.SlotName == MrpPlanSlotNames.Current
                                select (DateOnly?)run.AsOfDate).SingleOrDefaultAsync(ct);
        if (latestAsOf.HasValue && latestAsOf.Value >= today) return;
        db.MrpRefreshOutboxMessages.Add(new MrpRefreshOutboxMessage("CALENDAR", "TehranBusinessDate",
            null, null, now, now));
        await db.SaveChangesAsync(ct);
    }

    private async Task RecoverExpiredLeases(HadronDbContext db, DateTime now, CancellationToken ct)
    {
        var leaseMinutes = Math.Clamp(options.Value.LeaseMinutes, 1, 120);
        var expiredBefore = now.AddMinutes(-leaseMinutes);
        var expired = await db.MrpRefreshOutboxMessages.Where(x =>
            x.Status == MrpRefreshOutboxStatuses.Processing && x.StartedAtUtc < expiredBefore).ToListAsync(ct);
        foreach (var message in expired) message.Recover(now);
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
    }

    private async Task Complete(IReadOnlyList<Guid> ids, Guid runId, DateTime now, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var messages = await db.MrpRefreshOutboxMessages.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        foreach (var message in messages) message.MarkCompleted(runId, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task Retry(IReadOnlyList<Guid> ids, string code, string message, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.MrpRefreshOutboxMessages.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maxAttempts = Math.Clamp(options.Value.MaxAttempts, 1, 10);
        foreach (var row in rows)
        {
            if (row.AttemptCount >= maxAttempts) row.MarkFailed(code, message, now);
            else row.MarkRetry(code, message, now.AddSeconds(Math.Pow(2, row.AttemptCount) * 5));
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> TryLock(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtextextended(@name, 0))", connection);
        command.Parameters.AddWithValue("name", LockName);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task Unlock(NpgsqlConnection connection)
    {
        if (connection.State != ConnectionState.Open) return;
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtextextended(@name, 0))", connection);
            command.Parameters.AddWithValue("name", LockName);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Closing the dedicated connection also releases a session advisory lock.
        }
    }
}

public sealed class AutomaticMrpRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AutomaticMrpRefreshOptions> options,
    ILogger<AutomaticMrpRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        var delay = TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 1, 60));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AutomaticMrpRefreshProcessor>()
                    .ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic MRP refresh worker iteration failed.");
            }
            await Task.Delay(delay, stoppingToken);
        }
    }
}
