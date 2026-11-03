using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Scheduler.Application;

public static class SchedulerTelemetry
{
    public const string Name = "Scheduler";

    public static readonly ActivitySource ActivitySource = new(Name);

    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> Bookings = Meter.CreateCounter<long>(
        "scheduler.bookings", unit: "{booking}", description: "Booking requests handled, by outcome.");

    public static readonly Counter<long> BookingConflicts = Meter.CreateCounter<long>(
        "scheduler.booking.conflicts", unit: "{conflict}", description: "Insert attempts lost to a concurrent request, by resource.");

    public static readonly Counter<long> Reschedules = Meter.CreateCounter<long>(
        "scheduler.reschedules", unit: "{reschedule}", description: "Reschedule requests handled, by outcome.");

    public static readonly Counter<long> Cancellations = Meter.CreateCounter<long>(
        "scheduler.cancellations", unit: "{cancellation}", description: "Cancellation requests handled, by outcome.");

    public static void RecordConflict(Activity? activity, string resource)
    {
        activity?.AddEvent(new ActivityEvent(
            "conflict",
            tags: new ActivityTagsCollection { { "resource", resource } }));
        BookingConflicts.Add(1, new KeyValuePair<string, object?>("resource", resource));
    }

    public static void Record(Counter<long> counter, string outcome) =>
        counter.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
