using Scheduler.Application.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

public abstract record RescheduleAppointmentResult
{
    private RescheduleAppointmentResult()
    {
    }

    public sealed record Rescheduled(AppointmentDetails Appointment) : RescheduleAppointmentResult;

    public sealed record NotFound(string Resource, Guid Id) : RescheduleAppointmentResult;

    public sealed record ChangeRuleViolated(ChangeRuleViolation Violation) : RescheduleAppointmentResult;

    public sealed record RuleViolated(BookingRuleViolation Violation) : RescheduleAppointmentResult;

    public sealed record NoCapacity(CapacityShortage Shortage) : RescheduleAppointmentResult;

    public sealed record VehicleAlreadyBooked : RescheduleAppointmentResult;
}
