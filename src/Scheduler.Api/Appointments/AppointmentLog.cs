using Microsoft.AspNetCore.Http.HttpResults;

namespace Scheduler.Api.Appointments;

public static partial class AppointmentLog
{
    public const string Category = "Scheduler.Api.Appointments";

    public static string Reason(ProblemHttpResult problem) =>
        problem.ProblemDetails.Type?[(problem.ProblemDetails.Type.LastIndexOf(':') + 1)..] ?? "unknown";

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Appointment {AppointmentId} confirmed at dealership {DealershipId} with bay {ServiceBayId} and technician {TechnicianId}")]
    public static partial void BookingConfirmed(
        this ILogger logger, Guid appointmentId, Guid dealershipId, Guid serviceBayId, Guid technicianId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Booking rejected at dealership {DealershipId}: {Reason}")]
    public static partial void BookingRejected(this ILogger logger, string reason, Guid dealershipId);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Appointment {AppointmentId} rescheduled")]
    public static partial void AppointmentRescheduled(this ILogger logger, Guid appointmentId);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Reschedule of appointment {AppointmentId} rejected: {Reason}")]
    public static partial void RescheduleRejected(this ILogger logger, string reason, Guid appointmentId);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Information,
        Message = "Appointment {AppointmentId} cancelled")]
    public static partial void AppointmentCancelled(this ILogger logger, Guid appointmentId);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Cancellation of appointment {AppointmentId} rejected: {Reason}")]
    public static partial void CancelRejected(this ILogger logger, string reason, Guid appointmentId);
}
