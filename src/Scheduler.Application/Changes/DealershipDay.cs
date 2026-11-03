using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

internal static class DealershipDay
{
    public static TimeRange Window(Dealership dealership, DateOnly date) =>
        new(FirstValidInstant(dealership, date), FirstValidInstant(dealership, date.AddDays(1)));

    private static DateTimeOffset FirstValidInstant(Dealership dealership, DateOnly date)
    {
        for (var hour = 0; hour < 24; hour++)
        {
            if (dealership.ToUtc(date, new TimeOnly(hour, 0)) is { } instant)
            {
                return instant;
            }
        }

        throw new InvalidOperationException($"No valid local time exists on {date}.");
    }
}
