using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

public sealed class GetAlternativesHandler(
    IAppointmentChangeStore store,
    TimeProvider clock,
    SchedulingRules rules)
{
    public async Task<AvailabilityLookupResult> HandleAsync(
        Guid appointmentId,
        DateOnly? date,
        CancellationToken cancellationToken)
    {
        var appointment = await store.FindAppointmentAsync(appointmentId, cancellationToken);

        if (appointment is null)
        {
            return new AvailabilityLookupResult.NotFound("appointment", appointmentId);
        }

        var now = clock.GetUtcNow();

        if (ChangePolicy.Check(appointment, now, rules.ChangeCutoff) is { } violation)
        {
            return new AvailabilityLookupResult.ChangeRuleViolated(violation);
        }

        var references = await store.LoadReferencesAsync(
            appointment.DealershipId, appointment.ServiceTypeId, cancellationToken);

        if (references.Dealership is not { } dealership)
        {
            return new AvailabilityLookupResult.NotFound("dealership", appointment.DealershipId);
        }

        if (references.ServiceType is not { } serviceType)
        {
            return new AvailabilityLookupResult.NotFound("serviceType", appointment.ServiceTypeId);
        }

        var localDate = date ?? DateOnly.FromDateTime(dealership.ToLocalTime(appointment.StartAt).DateTime);

        var schedule = await store.LoadDayScheduleAsync(
            dealership.Id, appointment.VehicleId, DealershipDay.Window(dealership, localDate), cancellationToken);

        var availability = AvailabilityCalculator.Calculate(
            dealership,
            serviceType,
            localDate,
            now,
            schedule.Bays,
            schedule.Technicians,
            schedule.ConfirmedDealershipAppointments.ToList(),
            schedule.ConfirmedVehicleAppointments.ToList(),
            appointment.Id);

        return availability switch
        {
            AvailabilityResult.Slots slots => new AvailabilityLookupResult.Available(
                dealership, serviceType, localDate, slots.Ranges),
            _ => new AvailabilityLookupResult.ServiceNotOffered(),
        };
    }
}
