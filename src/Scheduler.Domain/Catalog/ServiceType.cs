using Scheduler.Domain.Resources;

namespace Scheduler.Domain.Catalog;

public sealed class ServiceType(Guid id, string name, int durationMinutes, BayType requiredBayType)
{
    public Guid Id { get; private set; } = id;

    public string Name { get; private set; } = name;

    public int DurationMinutes { get; private set; } = durationMinutes;

    public BayType RequiredBayType { get; private set; } = requiredBayType;

    public TimeSpan Duration => TimeSpan.FromMinutes(DurationMinutes);
}
