using Scheduler.Application.Appointments;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

public sealed class CancelAppointmentHandler(
    IAppointmentChangeStore store,
    IAppointmentQueries queries,
    TimeProvider clock,
    SchedulingRules rules)
{
    public async Task<CancelAppointmentResult> HandleAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var outcome = await store.TryCancelAsync(appointmentId, clock.GetUtcNow(), rules.ChangeCutoff, cancellationToken);

        if (outcome == CancelOutcome.Cancelled)
        {
            return await CurrentDetailsAsync(appointmentId, cancellationToken);
        }

        var current = await store.FindAppointmentAsync(appointmentId, cancellationToken);

        if (current is null)
        {
            return new CancelAppointmentResult.NotFound("appointment", appointmentId);
        }

        if (current.Status == AppointmentStatus.Cancelled)
        {
            return await CurrentDetailsAsync(appointmentId, cancellationToken);
        }

        return new CancelAppointmentResult.ChangeRuleViolated(ChangeRuleViolation.ChangeWindowClosed);
    }

    private async Task<CancelAppointmentResult> CurrentDetailsAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var details = await queries.FindByIdAsync(appointmentId, cancellationToken);

        return details is null
            ? new CancelAppointmentResult.NotFound("appointment", appointmentId)
            : new CancelAppointmentResult.Cancelled(details);
    }
}
