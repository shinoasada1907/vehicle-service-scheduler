namespace Scheduler.Application.Booking;

public sealed record BookAppointmentCommand(
    Guid CustomerId,
    Guid VehicleId,
    Guid DealershipId,
    Guid ServiceTypeId,
    DateTimeOffset StartAt);
