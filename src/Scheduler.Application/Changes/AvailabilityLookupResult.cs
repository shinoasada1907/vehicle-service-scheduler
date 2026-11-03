using Scheduler.Domain.Catalog;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

public abstract record AvailabilityLookupResult
{
    private AvailabilityLookupResult()
    {
    }

    public sealed record Available(
        Dealership Dealership,
        ServiceType ServiceType,
        DateOnly Date,
        IReadOnlyList<TimeRange> Slots) : AvailabilityLookupResult;

    public sealed record NotFound(string Resource, Guid Id) : AvailabilityLookupResult;

    public sealed record ServiceNotOffered : AvailabilityLookupResult;

    public sealed record ChangeRuleViolated(ChangeRuleViolation Violation) : AvailabilityLookupResult;
}
