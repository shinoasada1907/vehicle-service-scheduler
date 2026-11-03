using Scheduler.Application.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

public abstract record CancelAppointmentResult
{
    private CancelAppointmentResult()
    {
    }

    public sealed record Cancelled(AppointmentDetails Appointment) : CancelAppointmentResult;

    public sealed record NotFound(string Resource, Guid Id) : CancelAppointmentResult;

    public sealed record ChangeRuleViolated(ChangeRuleViolation Violation) : CancelAppointmentResult;
}
