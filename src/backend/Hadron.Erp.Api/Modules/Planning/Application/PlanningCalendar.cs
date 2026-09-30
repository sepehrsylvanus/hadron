using System.Globalization;

namespace Hadron.Erp.Api.Modules.Planning.Application;

public interface IPlanningCalendar
{
    DateOnly TehranToday { get; }
    (DateOnly StartDate, DateOnly EndDate) GetPeriodBounds(int jalaliYear, int jalaliMonth);
    DateOnly GetGregorianDate(int jalaliYear, int jalaliMonth, int jalaliDay);
    (int JalaliYear, int JalaliMonth, int JalaliDay) GetJalaliDate(DateOnly gregorianDate);
    (int JalaliYear, int JalaliMonth) GetJalaliPeriod(DateOnly gregorianDate);
}

public sealed class PlanningCalendar(TimeProvider timeProvider) : IPlanningCalendar
{
    private static readonly PersianCalendar Persian = new();
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public DateOnly TehranToday => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Tehran).DateTime);

    public (DateOnly StartDate, DateOnly EndDate) GetPeriodBounds(int jalaliYear, int jalaliMonth)
    {
        if (jalaliMonth is < 1 or > 12)
        {
            throw PlanningValidationException.Single("jalaliMonth", "ماه جلالی باید بین ۱ و ۱۲ باشد.");
        }

        try
        {
            var days = Persian.GetDaysInMonth(jalaliYear, jalaliMonth);
            var start = Persian.ToDateTime(jalaliYear, jalaliMonth, 1, 0, 0, 0, 0);
            var end = Persian.ToDateTime(jalaliYear, jalaliMonth, days, 0, 0, 0, 0);
            return (DateOnly.FromDateTime(start), DateOnly.FromDateTime(end));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw PlanningValidationException.Single("jalaliYear", "سال جلالی معتبر نیست.");
        }
    }

    public (int JalaliYear, int JalaliMonth) GetJalaliPeriod(DateOnly gregorianDate)
    {
        var jalali = GetJalaliDate(gregorianDate);
        return (jalali.JalaliYear, jalali.JalaliMonth);
    }

    public DateOnly GetGregorianDate(int jalaliYear, int jalaliMonth, int jalaliDay)
    {
        try
        {
            return DateOnly.FromDateTime(Persian.ToDateTime(
                jalaliYear, jalaliMonth, jalaliDay, 0, 0, 0, 0));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw PlanningValidationException.Single("jalaliDate", "تاریخ شمسی معتبر نیست.");
        }
    }

    public (int JalaliYear, int JalaliMonth, int JalaliDay) GetJalaliDate(DateOnly gregorianDate)
    {
        var date = gregorianDate.ToDateTime(TimeOnly.MinValue);
        return (Persian.GetYear(date), Persian.GetMonth(date), Persian.GetDayOfMonth(date));
    }
}
