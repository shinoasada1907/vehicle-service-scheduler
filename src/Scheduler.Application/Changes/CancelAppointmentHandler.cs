using System.Diagnostics;
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
        using var activity = SchedulerTelemetry.ActivitySource.StartActivity("scheduler.cancel");
        activity?.SetTag("scheduler.appointment_id", appointmentId);

        var (result, outcome) = await CancelAsync(appointmentId, cancellationToken);

        activity?.SetTag("scheduler.outcome", outcome);
        SchedulerTelemetry.Record(SchedulerTelemetry.Cancellations, outcome);

        return result;
    }

    private async Task<(CancelAppointmentResult Result, string Outcome)> CancelAsync(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var outcome = await store.TryCancelAsync(appointmentId, clock.GetUtcNow(), rules.ChangeCutoff, cancellationToken);

        if (outcome == CancelOutcome.Cancelled)
        {
            return await CurrentDetailsAsync(appointmentId, "cancelled", cancellationToken);
        }

        var current = await store.FindAppointmentAsync(appointmentId, cancellationToken);

        if (current is null)
        {
            return (new CancelAppointmentResult.NotFound("appointment", appointmentId), "not-found");
        }

        if (current.Status == AppointmentStatus.Cancelled)
        {
            return await CurrentDetailsAsync(appointmentId, "already-cancelled", cancellationToken);
        }

        return (new CancelAppointmentResult.ChangeRuleViolated(ChangeRuleViolation.ChangeWindowClosed), "window-closed");
    }

    private async Task<(CancelAppointmentResult Result, string Outcome)> CurrentDetailsAsync(
        Guid appointmentId,
        string foundOutcome,
        CancellationToken cancellationToken)
    {
        var details = await queries.FindByIdAsync(appointmentId, cancellationToken);

        return details is null
            ? (new CancelAppointmentResult.NotFound("appointment", appointmentId), "not-found")
            : (new CancelAppointmentResult.Cancelled(details), foundOutcome);
    }
}
