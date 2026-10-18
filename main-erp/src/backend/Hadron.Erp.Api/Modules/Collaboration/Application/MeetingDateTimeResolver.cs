using System.Globalization;
using Hadron.Erp.Api.Modules.Collaboration.Domain;
using Hadron.Erp.Api.Modules.Planning.Application;

namespace Hadron.Erp.Api.Modules.Collaboration.Application;

public sealed class MeetingDateTimeResolver(IPlanningCalendar calendar)
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");

    public ResolveMeetingJalaliDateTimeResponse Resolve(ResolveMeetingJalaliDateTimeRequest request)
    {
        var date = NormalizeDigits(request.JalaliDate).Trim();
        var time = NormalizeDigits(request.LocalTime).Trim();
        var errors = new Dictionary<string, string[]>();

        var year = 0;
        var month = 0;
        var day = 0;
        if (date.Length != 8 || date.Any(character => !char.IsAsciiDigit(character)) ||
            !int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
            !int.TryParse(date.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month) ||
            !int.TryParse(date.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out day))
            errors["jalaliDate"] = ["تاریخ باید ۸ رقم و با قالب YYYYMMDD باشد؛ مانند 14050617."];

        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localTime))
            errors["localTime"] = ["ساعت باید با قالب ۲۴ ساعته HH:mm باشد؛ مانند 14:30."];

        if (errors.Count > 0) throw new CollaborationValidationException(errors);

        DateOnly gregorianDate;
        try
        {
            gregorianDate = calendar.GetGregorianDate(year, month, day);
        }
        catch (PlanningValidationException)
        {
            throw new CollaborationValidationException(new Dictionary<string, string[]>
                { ["jalaliDate"] = ["تاریخ شمسی معتبر نیست."] });
        }

        var localDateTime = DateTime.SpecifyKind(gregorianDate.ToDateTime(localTime), DateTimeKind.Unspecified);
        if (Tehran.IsInvalidTime(localDateTime) || Tehran.IsAmbiguousTime(localDateTime))
            throw new CollaborationValidationException(new Dictionary<string, string[]>
                { ["localTime"] = ["این ساعت در منطقه زمانی تهران یکتا و معتبر نیست."] });

        var utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, Tehran);
        return new(date, localTime.ToString("HH:mm", CultureInfo.InvariantCulture), gregorianDate, utc,
            Tehran.Id);
    }

    private static string NormalizeDigits(string? value) => string.Concat((value ?? string.Empty).Select(character =>
        character switch
        {
            >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
            >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
            _ => character
        }));
}
