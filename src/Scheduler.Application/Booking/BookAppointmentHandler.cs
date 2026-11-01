using Scheduler.Application.Appointments;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Booking;

public sealed class BookAppointmentHandler(IBookingStore store, TimeProvider clock)
{
    public const int MaxAttempts = 3;

    public async Task<BookAppointmentResult> HandleAsync(
        BookAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var references = await store.LoadReferencesAsync(command, cancellationToken);

        if (references.Dealership is not { } dealership)
        {
            return new BookAppointmentResult.NotFound("dealership", command.DealershipId);
        }

        if (references.ServiceType is not { } serviceType)
        {
            return new BookAppointmentResult.NotFound("serviceType", command.ServiceTypeId);
        }

        if (!references.CustomerExists)
        {
            return new BookAppointmentResult.NotFound("customer", command.CustomerId);
        }

        if (references.Vehicle is not { } vehicle)
        {
            return new BookAppointmentResult.NotFound("vehicle", command.VehicleId);
        }

        var now = clock.GetUtcNow();
        var period = TimeRange.FromDuration(command.StartAt.ToUniversalTime(), serviceType.Duration);

        if (BookingPolicy.Check(dealership, vehicle, command.CustomerId, period, now) is { } violation)
        {
            return new BookAppointmentResult.RuleViolated(violation);
        }

        var lostRaceFor = CapacityShortage.Bays;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var schedule = await store.LoadScheduleAsync(dealership.Id, vehicle.Id, period, cancellationToken);

            if (schedule.ConfirmedVehicleAppointments.Any(existing => existing.Period.Overlaps(period)))
            {
                return new BookAppointmentResult.VehicleAlreadyBooked();
            }

            var allocation = ResourceAllocator.Allocate(
                serviceType,
                period,
                schedule.Bays,
                schedule.Technicians,
                schedule.ConfirmedDealershipAppointments);

            if (allocation is not AllocationResult.Allocated allocated)
            {
                return allocation switch
                {
                    AllocationResult.NoCapacity noCapacity => new BookAppointmentResult.NoCapacity(noCapacity.Shortage),
                    _ => new BookAppointmentResult.RuleViolated(BookingRuleViolation.ServiceNotOffered),
                };
            }

            var appointment = Appointment.Confirm(
                Guid.CreateVersion7(now),
                dealership.Id,
                command.CustomerId,
                vehicle.Id,
                serviceType.Id,
                allocated.Bay.Id,
                allocated.Technician.Id,
                period,
                now);

            switch (await store.TryAddAsync(appointment, cancellationToken))
            {
                case AddAppointmentResult.Added:
                    return new BookAppointmentResult.Booked(AppointmentDetails.From(
                        appointment, dealership, serviceType, allocated.Bay, allocated.Technician));
                case AddAppointmentResult.VehicleTaken:
                    return new BookAppointmentResult.VehicleAlreadyBooked();
                case AddAppointmentResult.BayTaken:
                    lostRaceFor = CapacityShortage.Bays;
                    break;
                case AddAppointmentResult.TechnicianTaken:
                    lostRaceFor = CapacityShortage.Technicians;
                    break;
            }
        }

        return new BookAppointmentResult.NoCapacity(lostRaceFor);
    }
}
