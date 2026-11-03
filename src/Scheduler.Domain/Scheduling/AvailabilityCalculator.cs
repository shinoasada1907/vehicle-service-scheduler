using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;

namespace Scheduler.Domain.Scheduling;

public static class AvailabilityCalculator
{
    public static AvailabilityResult Calculate(
        Dealership dealership,
        ServiceType serviceType,
        DateOnly localDate,
        DateTimeOffset now,
        IEnumerable<ServiceBay> bays,
        IEnumerable<Technician> technicians,
        IReadOnlyCollection<Appointment> confirmedDealershipAppointments,
        IReadOnlyCollection<Appointment> confirmedVehicleAppointments,
        Guid? ignoredAppointmentId)
    {
        var bayList = bays.ToList();
        var technicianList = technicians.ToList();

        if (!bayList.Any(bay => bay.Supports(serviceType))
            || !technicianList.Any(technician => technician.IsQualifiedFor(serviceType)))
        {
            return new AvailabilityResult.ServiceNotOffered();
        }

        var openingHours = dealership.OpeningHoursOn(localDate);

        if (openingHours is null)
        {
            return new AvailabilityResult.Slots([]);
        }

        var dealershipAppointments = confirmedDealershipAppointments
            .Where(appointment => appointment.Id != ignoredAppointmentId)
            .ToList();
        var vehicleAppointments = confirmedVehicleAppointments
            .Where(appointment => appointment.Id != ignoredAppointmentId)
            .ToList();

        var duration = serviceType.Duration;
        var lastStart = openingHours.Closes.ToTimeSpan() - duration;
        var ranges = new List<TimeRange>();

        for (var localStart = openingHours.Opens.ToTimeSpan();
             localStart <= lastStart;
             localStart += BookingPolicy.SlotLength)
        {
            var start = dealership.ToUtc(localDate, TimeOnly.FromTimeSpan(localStart));

            if (start is null || start.Value <= now)
            {
                continue;
            }

            var range = TimeRange.FromDuration(start.Value, duration);

            if (vehicleAppointments.Any(appointment => appointment.Period.Overlaps(range)))
            {
                continue;
            }

            var allocation = ResourceAllocator.Allocate(
                serviceType, range, bayList, technicianList, dealershipAppointments);

            if (allocation is AllocationResult.Allocated)
            {
                ranges.Add(range);
            }
        }

        return new AvailabilityResult.Slots(ranges);
    }
}
