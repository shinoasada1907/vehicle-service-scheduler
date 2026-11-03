using Scheduler.Application.Changes;

namespace Scheduler.Api.Appointments;

public sealed record AvailabilityResponse(
    Guid DealershipId,
    Guid ServiceTypeId,
    string Date,
    string TimeZone,
    IReadOnlyList<SlotResponse> Slots)
{
    public static AvailabilityResponse From(AvailabilityLookupResult.Available available) => new(
        available.Dealership.Id,
        available.ServiceType.Id,
        available.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        available.Dealership.TimeZoneId,
        available.Slots.Select(slot => new SlotResponse(slot.Start, slot.End)).ToList());
}

public sealed record SlotResponse(DateTimeOffset StartAt, DateTimeOffset EndAt);
