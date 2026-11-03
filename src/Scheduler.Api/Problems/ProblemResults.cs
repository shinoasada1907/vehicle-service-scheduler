using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Api.Problems;

public static class ProblemResults
{
    private const string TypePrefix = "urn:vehicle-service-scheduler:problem:";

    public const string ValidationType = TypePrefix + "validation";

    public static ProblemHttpResult NotFound(HttpContext httpContext, string resource, Guid id) =>
        Create(
            httpContext,
            StatusCodes.Status404NotFound,
            "not-found",
            "Resource not found",
            $"The {resource} '{id}' was not found.");

    public static ProblemHttpResult RuleViolated(HttpContext httpContext, BookingRuleViolation violation)
    {
        var (slug, title, detail) = violation switch
        {
            BookingRuleViolation.VehicleNotOwnedByCustomer => (
                "vehicle-not-owned",
                "Vehicle not owned by the customer",
                "The vehicle does not belong to the customer making the booking."),
            BookingRuleViolation.StartInPast => (
                "invalid-start-time",
                "Invalid start time",
                "The start time must be in the future."),
            BookingRuleViolation.StartNotOnSlotBoundary => (
                "invalid-start-time",
                "Invalid start time",
                "The start time must be on a 15-minute boundary in the dealership's local time."),
            BookingRuleViolation.OutsideOpeningHours => (
                "outside-opening-hours",
                "Outside opening hours",
                "The whole service must fall within the dealership's opening hours on a single day."),
            BookingRuleViolation.ServiceNotOffered => (
                "service-not-offered",
                "Service not offered",
                "The dealership cannot perform this service type."),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null),
        };

        return Create(httpContext, StatusCodes.Status422UnprocessableEntity, slug, title, detail);
    }

    public static ProblemHttpResult ChangeRuleViolated(
        HttpContext httpContext,
        ChangeRuleViolation violation,
        TimeSpan cutoff)
    {
        var (slug, title, detail) = violation switch
        {
            ChangeRuleViolation.ChangeWindowClosed => (
                "change-window-closed",
                "Change window closed",
                $"Appointments can be changed or cancelled only more than {DescribeCutoff(cutoff)} before they start."),
            ChangeRuleViolation.AppointmentCancelled => (
                "appointment-cancelled",
                "Appointment cancelled",
                "The appointment has been cancelled and can no longer be changed."),
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, null),
        };

        return Create(httpContext, StatusCodes.Status422UnprocessableEntity, slug, title, detail);
    }

    public static ProblemHttpResult NoCapacity(HttpContext httpContext, CapacityShortage shortage)
    {
        var shortageName = shortage switch
        {
            CapacityShortage.Bays => "bays",
            CapacityShortage.Technicians => "technicians",
            CapacityShortage.BaysAndTechnicians => "bays-and-technicians",
            _ => throw new ArgumentOutOfRangeException(nameof(shortage), shortage, null),
        };

        var extensions = TraceExtensions(httpContext);
        extensions["shortage"] = shortageName;

        return Create(
            httpContext,
            StatusCodes.Status409Conflict,
            "no-capacity",
            "No capacity available",
            "No compatible service bay and qualified technician are free for the whole service duration.",
            extensions);
    }

    public static ProblemHttpResult VehicleAlreadyBooked(HttpContext httpContext) =>
        Create(
            httpContext,
            StatusCodes.Status409Conflict,
            "vehicle-already-booked",
            "Vehicle already booked",
            "The vehicle already has a confirmed appointment that overlaps the requested time.");

    public static ProblemHttpResult IdempotencyKeyReused(HttpContext httpContext) =>
        Create(
            httpContext,
            StatusCodes.Status422UnprocessableEntity,
            "idempotency-key-reused",
            "Idempotency key reused",
            "The Idempotency-Key was already used for a different booking request.");

    public static Dictionary<string, object?> TraceExtensions(HttpContext httpContext) =>
        new() { ["traceId"] = TraceId(httpContext) };

    public static void Normalize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Extensions["traceId"] = TraceId(context.HttpContext);

        if (problem.Type?.StartsWith(TypePrefix, StringComparison.Ordinal) == true)
        {
            return;
        }

        var status = problem.Status ?? context.HttpContext.Response.StatusCode;

        var mapped = status switch
        {
            StatusCodes.Status400BadRequest => (
                "validation",
                "Invalid request",
                "The request body is malformed or a field has the wrong type."),
            StatusCodes.Status404NotFound => (
                "not-found",
                "Resource not found",
                "The requested resource was not found."),
            StatusCodes.Status405MethodNotAllowed => (
                "method-not-allowed",
                "Method not allowed",
                "The HTTP method is not allowed for this resource."),
            StatusCodes.Status415UnsupportedMediaType => (
                "unsupported-media-type",
                "Unsupported media type",
                "The request body must be JSON (Content-Type: application/json)."),
            >= 500 and <= 599 => (
                "internal-error",
                "Internal server error",
                "An unexpected error occurred."),
            _ => default((string, string, string)?),
        };

        if (mapped is var (slug, title, detail))
        {
            problem.Type = TypePrefix + slug;
            problem.Title = title;
            problem.Detail = detail;
        }
        else if (string.IsNullOrWhiteSpace(problem.Detail))
        {
            problem.Detail = problem.Title;
        }
    }

    private static string DescribeCutoff(TimeSpan cutoff)
    {
        if (cutoff.TotalHours >= 1 && cutoff.TotalHours % 1 == 0)
        {
            var hours = (int)cutoff.TotalHours;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }

        var minutes = (int)Math.Ceiling(cutoff.TotalMinutes);
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }

    private static string TraceId(HttpContext httpContext) =>
        Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;

    private static ProblemHttpResult Create(
        HttpContext httpContext,
        int status,
        string slug,
        string title,
        string detail,
        Dictionary<string, object?>? extensions = null) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: status,
            title: title,
            type: TypePrefix + slug,
            extensions: extensions ?? TraceExtensions(httpContext));
}
