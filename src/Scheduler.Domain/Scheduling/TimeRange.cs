namespace Scheduler.Domain.Scheduling;

public readonly record struct TimeRange
{
    public TimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (start.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Start must be in UTC.", nameof(start));
        }

        if (end.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("End must be in UTC.", nameof(end));
        }

        if (end <= start)
        {
            throw new ArgumentException("End must be after start.", nameof(end));
        }

        Start = start;
        End = end;
    }

    public DateTimeOffset Start { get; }

    public DateTimeOffset End { get; }

    public static TimeRange FromDuration(DateTimeOffset start, TimeSpan duration) => new(start, start + duration);

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
}
