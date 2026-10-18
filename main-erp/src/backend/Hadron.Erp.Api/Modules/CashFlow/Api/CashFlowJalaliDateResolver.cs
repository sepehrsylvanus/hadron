using System.Globalization;
using Hadron.Erp.Api.Modules.CashFlow.Application;
using Hadron.Erp.Api.Modules.Planning.Application;

namespace Hadron.Erp.Api.Modules.CashFlow.Api;

internal static class CashFlowJalaliDateResolver
{
    public static IResult Resolve(string? jalaliDate, IPlanningCalendar calendar)
    {
        try
        {
            var normalized = NormalizeDigits(jalaliDate ?? string.Empty).Trim();
            if (normalized.Length != 8 || normalized.Any(x => !char.IsAsciiDigit(x)) ||
                !int.TryParse(normalized.AsSpan(0, 4), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var year) ||
                !int.TryParse(normalized.AsSpan(4, 2), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var month) ||
                !int.TryParse(normalized.AsSpan(6, 2), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var day))
            {
                throw Validation();
            }

            return Results.Ok(new CashFlowJalaliDateResponse(normalized,
                calendar.GetGregorianDate(year, month, day)));
        }
        catch (PlanningValidationException validation)
        {
            return Results.ValidationProblem(validation.Errors);
        }
        catch (CashFlowValidationException validation)
        {
            return Results.ValidationProblem(validation.Errors);
        }
    }

    private static CashFlowValidationException Validation() => new(
        new Dictionary<string, string[]>
        {
            ["jalaliDate"] = ["تاریخ باید با قالب YYYYMMDD وارد شود؛ مانند 14050616."]
        });

    private static string NormalizeDigits(string value)
    {
        Span<char> normalized = stackalloc char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            normalized[index] = value[index] switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + value[index] - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + value[index] - '\u0660'),
                _ => value[index]
            };
        }

        return new string(normalized);
    }
}
