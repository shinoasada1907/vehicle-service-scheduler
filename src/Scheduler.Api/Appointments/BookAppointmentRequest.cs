using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using Scheduler.Application.Booking;

namespace Scheduler.Api.Appointments;

public sealed partial record BookAppointmentRequest(
    Guid? CustomerId,
    Guid? VehicleId,
    Guid? DealershipId,
    Guid? ServiceTypeId,
    [property: Description("ISO 8601 timestamp with an explicit UTC offset, for example 2030-01-07T09:00:00Z.")]
    string? StartAt)
{
    public bool TryCreateCommand(
        [NotNullWhen(true)] out BookAppointmentCommand? command,
        out Dictionary<string, string[]> errors)
    {
        errors = [];

        var customerId = Require(CustomerId, "customerId", errors);
        var vehicleId = Require(VehicleId, "vehicleId", errors);
        var dealershipId = Require(DealershipId, "dealershipId", errors);
        var serviceTypeId = Require(ServiceTypeId, "serviceTypeId", errors);
        var startAt = default(DateTimeOffset);

        if (string.IsNullOrWhiteSpace(StartAt))
        {
            errors["startAt"] = ["startAt is required."];
        }
        else if (!ExplicitOffset().IsMatch(StartAt)
            || !DateTimeOffset.TryParse(StartAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out startAt))
        {
            errors["startAt"] =
            [
                "startAt must be an ISO 8601 timestamp with an explicit UTC offset, for example 2030-01-07T09:00:00Z.",
            ];
        }

        command = errors.Count == 0
            ? new BookAppointmentCommand(customerId, vehicleId, dealershipId, serviceTypeId, startAt.ToUniversalTime())
            : null;

        return command is not null;
    }

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex ExplicitOffset();

    private static Guid Require(Guid? value, string field, Dictionary<string, string[]> errors)
    {
        if (value is { } id)
        {
            return id;
        }

        errors[field] = [$"{field} is required."];

        return Guid.Empty;
    }
}
