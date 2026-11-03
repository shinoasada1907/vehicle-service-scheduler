namespace Scheduler.Domain.Scheduling;

public sealed record SchedulingRules(TimeSpan ChangeCutoff)
{
    public static readonly SchedulingRules Default = new(TimeSpan.FromHours(2));
}
