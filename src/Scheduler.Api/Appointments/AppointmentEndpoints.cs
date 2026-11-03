using Microsoft.AspNetCore.Http.HttpResults;
using Scheduler.Api.Problems;
using Scheduler.Application.Appointments;
using Scheduler.Application.Booking;
using Scheduler.Application.Changes;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Api.Appointments;

public static class AppointmentEndpoints
{
    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/appointments").WithTags("Appointments");

        group.MapPost("/", BookAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}", GetAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reschedule", RescheduleAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Results<Created<AppointmentDetails>, ValidationProblem, ProblemHttpResult>> BookAsync(
        BookAppointmentRequest request,
        BookAppointmentHandler handler,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!request.TryCreateCommand(out var command, out var errors))
        {
            return TypedResults.ValidationProblem(
                errors,
                detail: "One or more fields are missing or invalid.",
                type: ProblemResults.ValidationType,
                extensions: ProblemResults.TraceExtensions(httpContext));
        }

        var logger = loggerFactory.CreateLogger(AppointmentLog.Category);
        var result = await handler.HandleAsync(command, cancellationToken);

        return result switch
        {
            BookAppointmentResult.Booked booked => BookingConfirmed(logger, booked.Appointment),
            BookAppointmentResult.NotFound notFound => BookingRejected(
                logger, command.DealershipId, ProblemResults.NotFound(httpContext, notFound.Resource, notFound.Id)),
            BookAppointmentResult.RuleViolated violated => BookingRejected(
                logger, command.DealershipId, ProblemResults.RuleViolated(httpContext, violated.Violation)),
            BookAppointmentResult.NoCapacity noCapacity => BookingRejected(
                logger, command.DealershipId, ProblemResults.NoCapacity(httpContext, noCapacity.Shortage)),
            BookAppointmentResult.VehicleAlreadyBooked => BookingRejected(
                logger, command.DealershipId, ProblemResults.VehicleAlreadyBooked(httpContext)),
            _ => throw new InvalidOperationException($"Unexpected booking result '{result.GetType().Name}'."),
        };
    }

    private static async Task<Results<Ok<AppointmentDetails>, ValidationProblem, ProblemHttpResult>> RescheduleAsync(
        Guid id,
        RescheduleAppointmentRequest request,
        RescheduleAppointmentHandler handler,
        SchedulingRules rules,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!request.TryGetStart(out var startAt, out var errors))
        {
            return TypedResults.ValidationProblem(
                errors,
                detail: "One or more fields are missing or invalid.",
                type: ProblemResults.ValidationType,
                extensions: ProblemResults.TraceExtensions(httpContext));
        }

        var logger = loggerFactory.CreateLogger(AppointmentLog.Category);
        var result = await handler.HandleAsync(id, startAt, cancellationToken);

        return result switch
        {
            RescheduleAppointmentResult.Rescheduled rescheduled => AppointmentRescheduled(logger, rescheduled.Appointment),
            RescheduleAppointmentResult.NotFound notFound => RescheduleRejected(
                logger, id, ProblemResults.NotFound(httpContext, notFound.Resource, notFound.Id)),
            RescheduleAppointmentResult.ChangeRuleViolated violated => RescheduleRejected(
                logger, id, ProblemResults.ChangeRuleViolated(httpContext, violated.Violation, rules.ChangeCutoff)),
            RescheduleAppointmentResult.RuleViolated violated => RescheduleRejected(
                logger, id, ProblemResults.RuleViolated(httpContext, violated.Violation)),
            RescheduleAppointmentResult.NoCapacity noCapacity => RescheduleRejected(
                logger, id, ProblemResults.NoCapacity(httpContext, noCapacity.Shortage)),
            RescheduleAppointmentResult.VehicleAlreadyBooked => RescheduleRejected(
                logger, id, ProblemResults.VehicleAlreadyBooked(httpContext)),
            _ => throw new InvalidOperationException($"Unexpected reschedule result '{result.GetType().Name}'."),
        };
    }

    private static async Task<Results<Ok<AppointmentDetails>, ProblemHttpResult>> CancelAsync(
        Guid id,
        CancelAppointmentHandler handler,
        SchedulingRules rules,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(AppointmentLog.Category);
        var result = await handler.HandleAsync(id, cancellationToken);

        return result switch
        {
            CancelAppointmentResult.Cancelled cancelled => AppointmentCancelled(logger, cancelled.Appointment),
            CancelAppointmentResult.NotFound notFound => CancelRejected(
                logger, id, ProblemResults.NotFound(httpContext, notFound.Resource, notFound.Id)),
            CancelAppointmentResult.ChangeRuleViolated violated => CancelRejected(
                logger, id, ProblemResults.ChangeRuleViolated(httpContext, violated.Violation, rules.ChangeCutoff)),
            _ => throw new InvalidOperationException($"Unexpected cancel result '{result.GetType().Name}'."),
        };
    }

    private static Created<AppointmentDetails> BookingConfirmed(ILogger logger, AppointmentDetails appointment)
    {
        logger.BookingConfirmed(
            appointment.Id, appointment.Dealership.Id, appointment.ServiceBay.Id, appointment.Technician.Id);
        return TypedResults.Created($"/appointments/{appointment.Id}", appointment);
    }

    private static ProblemHttpResult BookingRejected(ILogger logger, Guid dealershipId, ProblemHttpResult problem)
    {
        logger.BookingRejected(AppointmentLog.Reason(problem), dealershipId);
        return problem;
    }

    private static Ok<AppointmentDetails> AppointmentRescheduled(ILogger logger, AppointmentDetails appointment)
    {
        logger.AppointmentRescheduled(appointment.Id);
        return TypedResults.Ok(appointment);
    }

    private static ProblemHttpResult RescheduleRejected(ILogger logger, Guid appointmentId, ProblemHttpResult problem)
    {
        logger.RescheduleRejected(AppointmentLog.Reason(problem), appointmentId);
        return problem;
    }

    private static Ok<AppointmentDetails> AppointmentCancelled(ILogger logger, AppointmentDetails appointment)
    {
        logger.AppointmentCancelled(appointment.Id);
        return TypedResults.Ok(appointment);
    }

    private static ProblemHttpResult CancelRejected(ILogger logger, Guid appointmentId, ProblemHttpResult problem)
    {
        logger.CancelRejected(AppointmentLog.Reason(problem), appointmentId);
        return problem;
    }

    private static async Task<Results<Ok<AppointmentDetails>, ProblemHttpResult>> GetAsync(
        Guid id,
        IAppointmentQueries queries,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (await queries.FindByIdAsync(id, cancellationToken) is not { } appointment)
        {
            return ProblemResults.NotFound(httpContext, "appointment", id);
        }

        return TypedResults.Ok(appointment);
    }
}
