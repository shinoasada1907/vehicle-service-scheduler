using System.Globalization;
using System.Text.RegularExpressions;

namespace Scheduler.Api.Appointments;

internal static partial class StartAtParser
{
    public static void TryParse(string? value, Dictionary<string, string[]> errors, out DateTimeOffset startAt)
    {
        startAt = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            errors["startAt"] = ["startAt is required."];
        }
        else if (!ExplicitOffset().IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out startAt))
        {
            errors["startAt"] =
            [
                "startAt must be an ISO 8601 timestamp with an explicit UTC offset, for example 2030-01-07T09:00:00Z.",
            ];
        }
    }

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex ExplicitOffset();
}
