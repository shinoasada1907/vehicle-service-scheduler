using System.Diagnostics;
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
        using var activity = SchedulerTelemetry.ActivitySource.StartActivity("scheduler.reschedule");
        activity?.SetTag("scheduler.appointment_id", appointmentId);
        activity?.SetTag("scheduler.attempts", 0);

        var (result, outcome) = await RescheduleAsync(appointmentId, newStart, activity, cancellationToken);

        activity?.SetTag("scheduler.outcome", outcome);
        SchedulerTelemetry.Record(SchedulerTelemetry.Reschedules, outcome);

        return result;
    }

    private static string OutcomeOf(ChangeRuleViolation violation) => violation switch
    {
        ChangeRuleViolation.AppointmentCancelled => "appointment-cancelled",
        _ => "change-window-closed",
    };

    private async Task<(RescheduleAppointmentResult Result, string Outcome)> RescheduleAsync(
        Guid appointmentId,
        DateTimeOffset newStart,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var current = await store.FindAppointmentAsync(appointmentId, cancellationToken);

        if (current is null)
        {
            return (new RescheduleAppointmentResult.NotFound("appointment", appointmentId), "not-found");
        }

        activity?.SetTag("scheduler.dealership_id", current.DealershipId);
        activity?.SetTag("scheduler.service_type_id", current.ServiceTypeId);

        if (ChangePolicy.Check(current, clock.GetUtcNow(), rules.ChangeCutoff) is { } changeViolation)
        {
            return (new RescheduleAppointmentResult.ChangeRuleViolated(changeViolation), OutcomeOf(changeViolation));
        }

        var start = newStart.ToUniversalTime();

        if (start == current.StartAt)
        {
            return await CurrentDetailsAsync(appointmentId, cancellationToken);
        }

        var references = await store.LoadReferencesAsync(current.DealershipId, current.ServiceTypeId, cancellationToken);

        if (references.Dealership is not { } dealership)
        {
            return (new RescheduleAppointmentResult.NotFound("dealership", current.DealershipId), "not-found");
        }

        if (references.ServiceType is not { } serviceType)
        {
            return (new RescheduleAppointmentResult.NotFound("serviceType", current.ServiceTypeId), "not-found");
        }

        var period = TimeRange.FromDuration(start, serviceType.Duration);

        if (BookingPolicy.CheckTiming(dealership, period, clock.GetUtcNow()) is { } bookingViolation)
        {
            return (new RescheduleAppointmentResult.RuleViolated(bookingViolation), "rule-violated");
        }

        var lastShortage = CapacityShortage.Bays;

        for (var attempt = 1; attempt <= BookAppointmentHandler.MaxAttempts; attempt++)
        {
            var schedule = await store.LoadDayScheduleAsync(
                dealership.Id, current.VehicleId, period, cancellationToken);

            if (schedule.ConfirmedVehicleAppointments.Any(
                    existing => existing.Id != current.Id && existing.Period.Overlaps(period)))
            {
                return (new RescheduleAppointmentResult.VehicleAlreadyBooked(), "vehicle-already-booked");
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
                    AllocationResult.NoCapacity noCapacity => (
                        new RescheduleAppointmentResult.NoCapacity(noCapacity.Shortage), "no-capacity"),
                    _ => (
                        new RescheduleAppointmentResult.RuleViolated(BookingRuleViolation.ServiceNotOffered),
                        "rule-violated"),
                };
            }

            activity?.SetTag("scheduler.attempts", attempt);

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
                    return (new RescheduleAppointmentResult.VehicleAlreadyBooked(), "vehicle-already-booked");
                case MoveOutcome.BayTaken:
                    lastShortage = CapacityShortage.Bays;
                    SchedulerTelemetry.RecordConflict(activity, "bay");
                    break;
                case MoveOutcome.TechnicianTaken:
                    lastShortage = CapacityShortage.Technicians;
                    SchedulerTelemetry.RecordConflict(activity, "technician");
                    break;
                case MoveOutcome.Deadlocked:
                    SchedulerTelemetry.RecordConflict(activity, "deadlock");
                    break;
                case MoveOutcome.NotChanged:
                    var reloaded = await store.FindAppointmentAsync(appointmentId, cancellationToken);

                    if (reloaded is null)
                    {
                        return (new RescheduleAppointmentResult.NotFound("appointment", appointmentId), "not-found");
                    }

                    if (ChangePolicy.Check(reloaded, clock.GetUtcNow(), rules.ChangeCutoff) is { } reloadedViolation)
                    {
                        return (
                            new RescheduleAppointmentResult.ChangeRuleViolated(reloadedViolation),
                            OutcomeOf(reloadedViolation));
                    }

                    current = reloaded;
                    break;
            }
        }

        return (new RescheduleAppointmentResult.NoCapacity(lastShortage), "no-capacity");
    }

    private async Task<(RescheduleAppointmentResult Result, string Outcome)> CurrentDetailsAsync(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var details = await queries.FindByIdAsync(appointmentId, cancellationToken);

        return details is null
            ? (new RescheduleAppointmentResult.NotFound("appointment", appointmentId), "not-found")
            : (new RescheduleAppointmentResult.Rescheduled(details), "rescheduled");
    }
}
