namespace Scheduler.Domain.Scheduling;

public abstract record AvailabilityResult
{
    private AvailabilityResult()
    {
    }

    public sealed record ServiceNotOffered : AvailabilityResult;

    public sealed record Slots(IReadOnlyList<TimeRange> Ranges) : AvailabilityResult;
}
