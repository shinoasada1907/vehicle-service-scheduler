namespace Scheduler.Domain.Dealerships;

public sealed record OpeningHours(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes);
