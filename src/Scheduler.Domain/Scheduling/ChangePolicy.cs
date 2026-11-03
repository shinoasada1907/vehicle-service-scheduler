using Scheduler.Domain.Appointments;

namespace Scheduler.Domain.Scheduling;

public static class ChangePolicy
{
    public static ChangeRuleViolation? Check(Appointment appointment, DateTimeOffset now, TimeSpan cutoff)
    {
        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            return ChangeRuleViolation.AppointmentCancelled;
        }

        if (appointment.StartAt - now <= cutoff)
        {
            return ChangeRuleViolation.ChangeWindowClosed;
        }

        return null;
    }
}
