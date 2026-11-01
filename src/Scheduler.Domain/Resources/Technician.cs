using Scheduler.Domain.Catalog;

namespace Scheduler.Domain.Resources;

public sealed class Technician
{
    private readonly List<Qualification> _qualifications = [];

    private Technician()
    {
    }

    public Technician(Guid id, Guid dealershipId, string name, IEnumerable<Guid> qualifiedServiceTypeIds)
    {
        Id = id;
        DealershipId = dealershipId;
        Name = name;
        _qualifications.AddRange(qualifiedServiceTypeIds.Select(serviceTypeId => new Qualification(serviceTypeId)));
    }

    public Guid Id { get; private set; }

    public Guid DealershipId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public IReadOnlyList<Qualification> Qualifications => _qualifications;

    public bool IsQualifiedFor(ServiceType serviceType) =>
        _qualifications.Any(qualification => qualification.ServiceTypeId == serviceType.Id);
}
