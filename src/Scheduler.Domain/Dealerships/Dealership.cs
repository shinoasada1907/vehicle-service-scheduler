using Scheduler.Domain.Scheduling;

namespace Scheduler.Domain.Dealerships;

public sealed class Dealership
{
    private readonly List<OpeningHours> _openingHours = [];

    private Dealership()
    {
    }

    public Dealership(Guid id, string name, string timeZoneId, IEnumerable<OpeningHours> openingHours)
    {
        Id = id;
        Name = name;
        TimeZoneId = timeZoneId;
        _openingHours.AddRange(openingHours);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string TimeZoneId { get; private set; } = string.Empty;

    public IReadOnlyList<OpeningHours> OpeningHours => _openingHours;

    public DateTimeOffset ToLocalTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId));

    public OpeningHours? OpeningHoursOn(DateOnly date) =>
        _openingHours.FirstOrDefault(hours => hours.Day == date.DayOfWeek);

    public DateTimeOffset? ToUtc(DateOnly date, TimeOnly time)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            return null;
        }

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    public bool IsOpenThroughout(TimeRange period)
    {
        var localStart = ToLocalTime(period.Start);
        var localEnd = ToLocalTime(period.End);

        if (localStart.Date != localEnd.Date)
        {
            return false;
        }

        var startTime = TimeOnly.FromTimeSpan(localStart.TimeOfDay);
        var endTime = TimeOnly.FromTimeSpan(localEnd.TimeOfDay);

        return _openingHours.Any(hours =>
            hours.Day == localStart.DayOfWeek && startTime >= hours.Opens && endTime <= hours.Closes);
    }
}
