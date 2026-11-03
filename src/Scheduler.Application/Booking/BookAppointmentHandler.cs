using System.Diagnostics;
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
        using var activity = SchedulerTelemetry.ActivitySource.StartActivity("scheduler.book");
        activity?.SetTag("scheduler.dealership_id", command.DealershipId);
        activity?.SetTag("scheduler.service_type_id", command.ServiceTypeId);
        activity?.SetTag("scheduler.attempts", 0);

        var (result, outcome) = await BookAsync(command, activity, cancellationToken);

        activity?.SetTag("scheduler.outcome", outcome);
        SchedulerTelemetry.Record(SchedulerTelemetry.Bookings, outcome);

        return result;
    }

    private static string OutcomeOf(CapacityShortage shortage) => shortage switch
    {
        CapacityShortage.Bays => "no-capacity-bays",
        CapacityShortage.Technicians => "no-capacity-technicians",
        _ => "no-capacity-bays-and-technicians",
    };

    private async Task<(BookAppointmentResult Result, string Outcome)> BookAsync(
        BookAppointmentCommand command,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var references = await store.LoadReferencesAsync(command, cancellationToken);

        if (references.Dealership is not { } dealership)
        {
            return (new BookAppointmentResult.NotFound("dealership", command.DealershipId), "not-found");
        }

        if (references.ServiceType is not { } serviceType)
        {
            return (new BookAppointmentResult.NotFound("serviceType", command.ServiceTypeId), "not-found");
        }

        if (!references.CustomerExists)
        {
            return (new BookAppointmentResult.NotFound("customer", command.CustomerId), "not-found");
        }

        if (references.Vehicle is not { } vehicle)
        {
            return (new BookAppointmentResult.NotFound("vehicle", command.VehicleId), "not-found");
        }

        var now = clock.GetUtcNow();
        var period = TimeRange.FromDuration(command.StartAt.ToUniversalTime(), serviceType.Duration);

        if (BookingPolicy.Check(dealership, vehicle, command.CustomerId, period, now) is { } violation)
        {
            return (new BookAppointmentResult.RuleViolated(violation), "rule-violated");
        }

        var lostRaceFor = CapacityShortage.Bays;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var schedule = await store.LoadScheduleAsync(dealership.Id, vehicle.Id, period, cancellationToken);

            if (schedule.ConfirmedVehicleAppointments.Any(existing => existing.Period.Overlaps(period)))
            {
                return (new BookAppointmentResult.VehicleAlreadyBooked(), "vehicle-already-booked");
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
                    AllocationResult.NoCapacity noCapacity => (
                        new BookAppointmentResult.NoCapacity(noCapacity.Shortage), OutcomeOf(noCapacity.Shortage)),
                    _ => (new BookAppointmentResult.RuleViolated(BookingRuleViolation.ServiceNotOffered), "rule-violated"),
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

            activity?.SetTag("scheduler.attempts", attempt);

            switch (await store.TryAddAsync(appointment, cancellationToken))
            {
                case AddAppointmentResult.Added:
                    return (
                        new BookAppointmentResult.Booked(AppointmentDetails.From(
                            appointment, dealership, serviceType, allocated.Bay, allocated.Technician)),
                        "confirmed");
                case AddAppointmentResult.VehicleTaken:
                    return (new BookAppointmentResult.VehicleAlreadyBooked(), "vehicle-already-booked");
                case AddAppointmentResult.BayTaken:
                    lostRaceFor = CapacityShortage.Bays;
                    SchedulerTelemetry.RecordConflict(activity, "bay");
                    break;
                case AddAppointmentResult.TechnicianTaken:
                    lostRaceFor = CapacityShortage.Technicians;
                    SchedulerTelemetry.RecordConflict(activity, "technician");
                    break;
                case AddAppointmentResult.Deadlocked:
                    SchedulerTelemetry.RecordConflict(activity, "deadlock");
                    break;
            }
        }

        return (new BookAppointmentResult.NoCapacity(lostRaceFor), OutcomeOf(lostRaceFor));
    }
}
