using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Scheduler.Application.Booking;

namespace Scheduler.Api.Appointments;

public sealed record BookAppointmentRequest(
    Guid? CustomerId,
    Guid? VehicleId,
    Guid? DealershipId,
    Guid? ServiceTypeId,
    [property: Description("ISO 8601 timestamp with an explicit UTC offset, for example 2030-01-07T09:00:00Z.")]
    string? StartAt)
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private const int MaxIdempotencyKeyLength = 100;

    public bool TryCreateCommand(
        string? idempotencyKey,
        [NotNullWhen(true)] out BookAppointmentCommand? command,
        out Dictionary<string, string[]> errors)
    {
        errors = [];

        if (idempotencyKey is not null && !IsValidIdempotencyKey(idempotencyKey))
        {
            errors[IdempotencyKeyHeader] =
                [$"{IdempotencyKeyHeader} must be 1 to {MaxIdempotencyKeyLength} visible ASCII characters."];
        }

        var customerId = Require(CustomerId, "customerId", errors);
        var vehicleId = Require(VehicleId, "vehicleId", errors);
        var dealershipId = Require(DealershipId, "dealershipId", errors);
        var serviceTypeId = Require(ServiceTypeId, "serviceTypeId", errors);

        StartAtParser.TryParse(StartAt, errors, out var startAt);

        command = errors.Count == 0
            ? new BookAppointmentCommand(customerId, vehicleId, dealershipId, serviceTypeId, startAt.ToUniversalTime(), idempotencyKey)
            : null;

        return command is not null;
    }

    private static bool IsValidIdempotencyKey(string key) =>
        key.Length is >= 1 and <= MaxIdempotencyKeyLength && key.All(character => character is >= '!' and <= '~');

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
