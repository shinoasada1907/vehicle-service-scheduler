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

        var result = await handler.HandleAsync(command, cancellationToken);

        return result switch
        {
            BookAppointmentResult.Booked booked =>
                TypedResults.Created($"/appointments/{booked.Appointment.Id}", booked.Appointment),
            BookAppointmentResult.NotFound notFound =>
                ProblemResults.NotFound(httpContext, notFound.Resource, notFound.Id),
            BookAppointmentResult.RuleViolated violated =>
                ProblemResults.RuleViolated(httpContext, violated.Violation),
            BookAppointmentResult.NoCapacity noCapacity =>
                ProblemResults.NoCapacity(httpContext, noCapacity.Shortage),
            BookAppointmentResult.VehicleAlreadyBooked =>
                ProblemResults.VehicleAlreadyBooked(httpContext),
            _ => throw new InvalidOperationException($"Unexpected booking result '{result.GetType().Name}'."),
        };
    }

    private static async Task<Results<Ok<AppointmentDetails>, ValidationProblem, ProblemHttpResult>> RescheduleAsync(
        Guid id,
        RescheduleAppointmentRequest request,
        RescheduleAppointmentHandler handler,
        SchedulingRules rules,
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

        var result = await handler.HandleAsync(id, startAt, cancellationToken);

        return result switch
        {
            RescheduleAppointmentResult.Rescheduled rescheduled => TypedResults.Ok(rescheduled.Appointment),
            RescheduleAppointmentResult.NotFound notFound =>
                ProblemResults.NotFound(httpContext, notFound.Resource, notFound.Id),
            RescheduleAppointmentResult.ChangeRuleViolated violated =>
                ProblemResults.ChangeRuleViolated(httpContext, violated.Violation, rules.ChangeCutoff),
            RescheduleAppointmentResult.RuleViolated violated =>
                ProblemResults.RuleViolated(httpContext, violated.Violation),
            RescheduleAppointmentResult.NoCapacity noCapacity =>
                ProblemResults.NoCapacity(httpContext, noCapacity.Shortage),
            RescheduleAppointmentResult.VehicleAlreadyBooked =>
                ProblemResults.VehicleAlreadyBooked(httpContext),
            _ => throw new InvalidOperationException($"Unexpected reschedule result '{result.GetType().Name}'."),
        };
    }

    private static async Task<Results<Ok<AppointmentDetails>, ProblemHttpResult>> CancelAsync(
        Guid id,
        CancelAppointmentHandler handler,
        SchedulingRules rules,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);

        return result switch
        {
            CancelAppointmentResult.Cancelled cancelled => TypedResults.Ok(cancelled.Appointment),
            CancelAppointmentResult.NotFound notFound =>
                ProblemResults.NotFound(httpContext, notFound.Resource, notFound.Id),
            CancelAppointmentResult.ChangeRuleViolated violated =>
                ProblemResults.ChangeRuleViolated(httpContext, violated.Violation, rules.ChangeCutoff),
            _ => throw new InvalidOperationException($"Unexpected cancel result '{result.GetType().Name}'."),
        };
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
