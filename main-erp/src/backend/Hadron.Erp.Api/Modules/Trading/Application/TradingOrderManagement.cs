using System.Globalization;
using System.Text.Json.Serialization;
using Hadron.Erp.Api.Infrastructure;
using Hadron.Erp.Api.Modules.Trading.Domain;
using Microsoft.EntityFrameworkCore;

namespace Hadron.Erp.Api.Modules.Trading.Application;

// This read contract keeps numeric(24,6) values exact in JavaScript clients.
public sealed record ManagedTradeOrder(Guid Id, Guid ProjectId, string RegistrationNumber, string Status,
    DateOnly RegistrationDate, string RegisteredAmount, Guid CurrencyId, string CurrencyCode,
    string CurrencyNameFa, string? CurrencyNameEn, string? Notes, long Version);
public sealed record TradeOrderPage(IReadOnlyList<ManagedTradeOrder> Items, int Page, int PageSize, int TotalCount);
public sealed record TradeOrderCurrencyPage(IReadOnlyList<TradingLookupItem> Items, int Page, int PageSize, int TotalCount);
public sealed record UpdateTradeOrderRequest(string RegistrationNumber, DateOnly RegistrationDate,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal RegisteredAmount,
    Guid CurrencyId, string? Notes, long ExpectedVersion, string Reason);

public sealed partial class TradingService
{
    public async Task<ManagedTradeOrder> GetManagedOrder(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.TradeOrderRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();
        return ManagedOrderOf(entity);
    }

    public async Task<TradeOrderPage> ListProjectOrders(Guid projectId, string? search, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        ValidateOrderPage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        await Project(db, projectId, ct); // Closed projects remain readable.
        var query = db.TradeOrderRegistrations.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 200) throw Validation("search", "جست‌وجو حداکثر ۲۰۰ کاراکتر است.");
            var normalizedTerm = term.ToUpperInvariant();
            query = query.Where(x => x.RegistrationNumber.ToUpper().Contains(normalizedTerm));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = Code(status, OrderStatuses, "status");
            query = query.Where(x => x.Status == normalized);
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(rows.Select(ManagedOrderOf).ToList(), page, pageSize, count);
    }

    public async Task<TradeOrderCurrencyPage> OrderCurrencies(string? search, int page, int pageSize, CancellationToken ct)
    {
        ValidateOrderPage(page, pageSize);
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Currencies.AsNoTracking().Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 200) throw Validation("search", "جست‌وجو حداکثر ۲۰۰ کاراکتر است.");
            var normalizedTerm = term.ToUpperInvariant();
            query = query.Where(x => x.Code.ToUpper().Contains(normalizedTerm) || x.NameFa.Contains(term) || (x.NameEn != null && x.NameEn.ToUpper().Contains(normalizedTerm)));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TradingLookupItem(x.Id, x.Code, x.NameFa, x.NameEn, true)).ToListAsync(ct);
        return new(rows, page, pageSize, count);
    }

    public async Task<ManagedTradeOrder> UpdateOrder(Guid id, UpdateTradeOrderRequest request, CancellationToken ct)
    {
        ValidateOrderDetails(request.RegistrationNumber, request.RegistrationDate, request.RegisteredAmount, request.Notes);
        Text(request.Reason, "reason", 1000);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entity = await LockOrder(db, id, ct) ?? throw new KeyNotFoundException();
        await OpenProject(db, entity.ProjectId, ct);
        Version(entity.Version, request.ExpectedVersion);
        if (entity.Status != "DRAFT") throw Conflict("ORDER_NOT_EDITABLE", "فقط سفارش پیش‌نویس قابل ویرایش اطلاعات است.");
        if (await db.TradeFxAllocations.AnyAsync(x => x.OrderRegistrationId == id, ct))
            throw Conflict("ORDER_HAS_FX", "این سفارش به پروندهٔ ارزی متصل است؛ اطلاعات مالی آن قابل بازنویسی نیست.");
        var before = Json(ManagedOrderOf(entity));
        if (request.CurrencyId != entity.CurrencyId)
        {
            var currency = await Currency(db, request.CurrencyId, ct);
            entity.EditDetails(request.RegistrationNumber.Trim(), request.RegistrationDate, request.RegisteredAmount,
                currency.Id, currency.Code, currency.NameFa, currency.NameEn, Clean(request.Notes));
        }
        else entity.EditDetails(request.RegistrationNumber.Trim(), request.RegistrationDate, request.RegisteredAmount,
            entity.CurrencyId, entity.CurrencyCodeSnapshot, entity.CurrencyNameFaSnapshot, entity.CurrencyNameEnSnapshot, Clean(request.Notes));
        AddAudit(db, "TradeProject", entity.ProjectId, "ORDER_REGISTRATION_EDIT", before, ManagedOrderOf(entity), request.Reason);
        await Save(db, ct);
        await tx.CommitAsync(ct);
        return ManagedOrderOf(entity);
    }

    // Shared by draft edit and FX linking: the relation check must not race a new FX file.
    private static Task<TradeOrderRegistration?> LockOrder(HadronDbContext db, Guid id, CancellationToken ct) =>
        db.TradeOrderRegistrations.FromSqlInterpolated($"SELECT * FROM trading.order_registrations WHERE \"Id\" = {id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

    private static ManagedTradeOrder ManagedOrderOf(TradeOrderRegistration x) => new(x.Id, x.ProjectId,
        x.RegistrationNumber, x.Status, x.RegistrationDate, x.RegisteredAmount.ToString("0.######", CultureInfo.InvariantCulture),
        x.CurrencyId, x.CurrencyCodeSnapshot, x.CurrencyNameFaSnapshot, x.CurrencyNameEnSnapshot, x.Notes, x.Version);

    private static void ValidateOrderPage(int page, int pageSize)
    {
        Page(page, pageSize);
        if ((long)(page - 1) * pageSize > int.MaxValue) throw Validation("page", "صفحه خارج از محدوده است.");
    }

    private static void ValidateOrderDetails(string number, DateOnly date, decimal amount, string? notes)
    {
        Text(number, "registrationNumber", 80);
        if (date == default) throw Validation("registrationDate", "تاریخ ثبت سفارش الزامی است.");
        if (amount <= 0 || amount >= 1_000_000_000_000_000_000m || decimal.Round(amount, 6) != amount)
            throw Validation("registeredAmount", "مبلغ باید مثبت و حداکثر ۱۸ رقم صحیح و ۶ رقم اعشار باشد.");
        if (notes?.Length > 2000) throw Validation("notes", "توضیحات حداکثر ۲۰۰۰ کاراکتر است.");
    }
}
