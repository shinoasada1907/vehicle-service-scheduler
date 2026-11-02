using Scheduler.Application.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Booking;

public abstract record BookAppointmentResult
{
    private BookAppointmentResult()
    {
    }

    public sealed record Booked(AppointmentDetails Appointment) : BookAppointmentResult;

    public sealed record NotFound(string Resource, Guid Id) : BookAppointmentResult;

    public sealed record RuleViolated(BookingRuleViolation Violation) : BookAppointmentResult;

    public sealed record NoCapacity(CapacityShortage Shortage) : BookAppointmentResult;

    public sealed record VehicleAlreadyBooked : BookAppointmentResult;
}
