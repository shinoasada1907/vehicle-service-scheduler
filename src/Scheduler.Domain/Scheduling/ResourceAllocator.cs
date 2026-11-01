using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Resources;

namespace Scheduler.Domain.Scheduling;

public static class ResourceAllocator
{
    public static AllocationResult Allocate(
        ServiceType serviceType,
        TimeRange period,
        IEnumerable<ServiceBay> bays,
        IEnumerable<Technician> technicians,
        IReadOnlyCollection<Appointment> confirmedAppointments)
    {
        var compatibleBays = bays
            .Where(bay => bay.Supports(serviceType))
            .OrderBy(bay => bay.Name, StringComparer.Ordinal)
            .ThenBy(bay => bay.Id)
            .ToList();

        var qualifiedTechnicians = technicians
            .Where(technician => technician.IsQualifiedFor(serviceType))
            .OrderBy(technician => technician.Name, StringComparer.Ordinal)
            .ThenBy(technician => technician.Id)
            .ToList();

        if (compatibleBays.Count == 0 || qualifiedTechnicians.Count == 0)
        {
            return new AllocationResult.ServiceNotOffered();
        }

        var overlapping = confirmedAppointments
            .Where(appointment => appointment.Period.Overlaps(period))
            .ToList();
        var busyBayIds = overlapping.Select(appointment => appointment.ServiceBayId).ToHashSet();
        var busyTechnicianIds = overlapping.Select(appointment => appointment.TechnicianId).ToHashSet();

        var freeBay = compatibleBays.FirstOrDefault(bay => !busyBayIds.Contains(bay.Id));
        var freeTechnician = qualifiedTechnicians.FirstOrDefault(technician => !busyTechnicianIds.Contains(technician.Id));

        if (freeBay is not null && freeTechnician is not null)
        {
            return new AllocationResult.Allocated(freeBay, freeTechnician);
        }

        var shortage = (freeBay, freeTechnician) switch
        {
            (null, null) => CapacityShortage.BaysAndTechnicians,
            (null, _) => CapacityShortage.Bays,
            _ => CapacityShortage.Technicians,
        };

        return new AllocationResult.NoCapacity(shortage);
    }
}
