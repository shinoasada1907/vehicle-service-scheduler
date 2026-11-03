using Scheduler.Application.Appointments;
using Scheduler.Application.Booking;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

public sealed class RescheduleAppointmentHandler(
    IAppointmentChangeStore store,
    IAppointmentQueries queries,
    TimeProvider clock,
    SchedulingRules rules)
{
    public async Task<RescheduleAppointmentResult> HandleAsync(
        Guid appointmentId,
        DateTimeOffset newStart,
        CancellationToken cancellationToken)
    {
        var current = await store.FindAppointmentAsync(appointmentId, cancellationToken);

        if (current is null)
        {
            return new RescheduleAppointmentResult.NotFound("appointment", appointmentId);
        }

        if (ChangePolicy.Check(current, clock.GetUtcNow(), rules.ChangeCutoff) is { } changeViolation)
        {
            return new RescheduleAppointmentResult.ChangeRuleViolated(changeViolation);
        }

        var start = newStart.ToUniversalTime();

        if (start == current.StartAt)
        {
            return await CurrentDetailsAsync(appointmentId, cancellationToken);
        }

        var references = await store.LoadReferencesAsync(current.DealershipId, current.ServiceTypeId, cancellationToken);

        if (references.Dealership is not { } dealership)
        {
            return new RescheduleAppointmentResult.NotFound("dealership", current.DealershipId);
        }

        if (references.ServiceType is not { } serviceType)
        {
            return new RescheduleAppointmentResult.NotFound("serviceType", current.ServiceTypeId);
        }

        var period = TimeRange.FromDuration(start, serviceType.Duration);

        if (BookingPolicy.CheckTiming(dealership, period, clock.GetUtcNow()) is { } bookingViolation)
        {
            return new RescheduleAppointmentResult.RuleViolated(bookingViolation);
        }

        var lastShortage = CapacityShortage.Bays;

        for (var attempt = 1; attempt <= BookAppointmentHandler.MaxAttempts; attempt++)
        {
            var schedule = await store.LoadDayScheduleAsync(
                dealership.Id, current.VehicleId, period, cancellationToken);

            if (schedule.ConfirmedVehicleAppointments.Any(
                    existing => existing.Id != current.Id && existing.Period.Overlaps(period)))
            {
                return new RescheduleAppointmentResult.VehicleAlreadyBooked();
            }

            var allocation = ResourceAllocator.Allocate(
                serviceType,
                period,
                schedule.Bays,
                schedule.Technicians,
                schedule.ConfirmedDealershipAppointments.Where(existing => existing.Id != current.Id).ToList());

            if (allocation is not AllocationResult.Allocated allocated)
            {
                return allocation switch
                {
                    AllocationResult.NoCapacity noCapacity => new RescheduleAppointmentResult.NoCapacity(noCapacity.Shortage),
                    _ => new RescheduleAppointmentResult.RuleViolated(BookingRuleViolation.ServiceNotOffered),
                };
            }

            var outcome = await store.TryMoveAsync(
                current,
                period,
                allocated.Bay.Id,
                allocated.Technician.Id,
                clock.GetUtcNow(),
                rules.ChangeCutoff,
                cancellationToken);

            switch (outcome)
            {
                case MoveOutcome.Moved:
                    return await CurrentDetailsAsync(appointmentId, cancellationToken);
                case MoveOutcome.VehicleTaken:
                    return new RescheduleAppointmentResult.VehicleAlreadyBooked();
                case MoveOutcome.BayTaken:
                    lastShortage = CapacityShortage.Bays;
                    break;
                case MoveOutcome.TechnicianTaken:
                    lastShortage = CapacityShortage.Technicians;
                    break;
                case MoveOutcome.Deadlocked:
                    break;
                case MoveOutcome.NotChanged:
                    var reloaded = await store.FindAppointmentAsync(appointmentId, cancellationToken);

                    if (reloaded is null)
                    {
                        return new RescheduleAppointmentResult.NotFound("appointment", appointmentId);
                    }

                    if (ChangePolicy.Check(reloaded, clock.GetUtcNow(), rules.ChangeCutoff) is { } reloadedViolation)
                    {
                        return new RescheduleAppointmentResult.ChangeRuleViolated(reloadedViolation);
                    }

                    current = reloaded;
                    break;
            }
        }

        return new RescheduleAppointmentResult.NoCapacity(lastShortage);
    }

    private async Task<RescheduleAppointmentResult> CurrentDetailsAsync(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var details = await queries.FindByIdAsync(appointmentId, cancellationToken);

        return details is null
            ? new RescheduleAppointmentResult.NotFound("appointment", appointmentId)
            : new RescheduleAppointmentResult.Rescheduled(details);
    }
}
