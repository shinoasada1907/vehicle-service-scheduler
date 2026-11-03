using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

public sealed class GetAvailabilityHandler(IAppointmentChangeStore store, TimeProvider clock)
{
    public async Task<AvailabilityLookupResult> HandleAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var references = await store.LoadReferencesAsync(dealershipId, serviceTypeId, cancellationToken);

        if (references.Dealership is not { } dealership)
        {
            return new AvailabilityLookupResult.NotFound("dealership", dealershipId);
        }

        if (references.ServiceType is not { } serviceType)
        {
            return new AvailabilityLookupResult.NotFound("serviceType", serviceTypeId);
        }

        var schedule = await store.LoadDayScheduleAsync(
            dealership.Id, null, DealershipDay.Window(dealership, date), cancellationToken);

        var availability = AvailabilityCalculator.Calculate(
            dealership,
            serviceType,
            date,
            clock.GetUtcNow(),
            schedule.Bays,
            schedule.Technicians,
            schedule.ConfirmedDealershipAppointments.ToList(),
            [],
            null);

        return availability switch
        {
            AvailabilityResult.Slots slots => new AvailabilityLookupResult.Available(
                dealership, serviceType, date, slots.Ranges),
            _ => new AvailabilityLookupResult.ServiceNotOffered(),
        };
    }
}
