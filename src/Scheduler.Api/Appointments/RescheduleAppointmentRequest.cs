using System.ComponentModel;

namespace Scheduler.Api.Appointments;

public sealed record RescheduleAppointmentRequest(
    [property: Description("ISO 8601 timestamp with an explicit UTC offset, for example 2030-01-07T09:30:00Z.")]
    string? StartAt)
{
    public bool TryGetStart(out DateTimeOffset startAt, out Dictionary<string, string[]> errors)
    {
        errors = [];
        StartAtParser.TryParse(StartAt, errors, out startAt);

        startAt = startAt.ToUniversalTime();

        return errors.Count == 0;
    }
}
