using Scheduler.Domain.Catalog;

namespace Scheduler.Domain.Resources;

public sealed class ServiceBay(Guid id, Guid dealershipId, string name, BayType bayType)
{
    public Guid Id { get; private set; } = id;

    public Guid DealershipId { get; private set; } = dealershipId;

    public string Name { get; private set; } = name;

    public BayType BayType { get; private set; } = bayType;

    public bool Supports(ServiceType serviceType) => BayType == serviceType.RequiredBayType;
}
