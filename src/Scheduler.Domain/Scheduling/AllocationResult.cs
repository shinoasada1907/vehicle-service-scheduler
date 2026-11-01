using Scheduler.Domain.Resources;

namespace Scheduler.Domain.Scheduling;

public abstract record AllocationResult
{
    private AllocationResult()
    {
    }

    public sealed record Allocated(ServiceBay Bay, Technician Technician) : AllocationResult;

    public sealed record ServiceNotOffered : AllocationResult;

    public sealed record NoCapacity(CapacityShortage Shortage) : AllocationResult;
}
