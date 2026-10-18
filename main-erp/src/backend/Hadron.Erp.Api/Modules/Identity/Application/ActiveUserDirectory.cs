using Hadron.Erp.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Identity.Application;

// Minimal Identity-owned read contract for assignment pickers. No admin account data is exposed.
public sealed record ActiveAssignmentUser(Guid Id, string FullName);
public sealed record ActiveAssignmentUserPage(IReadOnlyList<ActiveAssignmentUser> Items, int TotalCount);

public interface IActiveUserDirectory
{
    Task<ActiveAssignmentUserPage> Search(string? search, int page, int pageSize, CancellationToken ct);
    Task<ActiveAssignmentUser?> Find(Guid id, CancellationToken ct);
}

public sealed class ActiveUserDirectory(IDbContextFactory<HadronDbContext> factory) : IActiveUserDirectory
{
    public async Task<ActiveAssignmentUserPage> Search(string? search, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Users.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => (x.FirstName + " " + x.LastName).Contains(term));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ThenBy(x => x.Id)
            .Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new ActiveAssignmentUser(x.Id, (x.FirstName + " " + x.LastName).Trim())).ToListAsync(ct);
        return new(rows, total);
    }

    public async Task<ActiveAssignmentUser?> Find(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().Where(x => x.Id == id && x.IsActive)
            .Select(x => new ActiveAssignmentUser(x.Id, (x.FirstName + " " + x.LastName).Trim()))
            .SingleOrDefaultAsync(ct);
    }
}
