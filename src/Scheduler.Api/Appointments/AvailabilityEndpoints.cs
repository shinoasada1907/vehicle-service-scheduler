using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Scheduler.Api.Problems;
using Scheduler.Application.Changes;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Api.Appointments;

public static class AvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapAvailabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dealerships/{id:guid}/availability", GetAvailabilityAsync)
            .WithTags("Availability")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/appointments/{id:guid}/alternatives", GetAlternativesAsync)
            .WithTags("Availability")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Results<Ok<AvailabilityResponse>, ValidationProblem, ProblemHttpResult>> GetAvailabilityAsync(
        Guid id,
        string? serviceTypeId,
        string? date,
        GetAvailabilityHandler handler,
        SchedulingRules rules,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var serviceType = default(Guid);

        if (string.IsNullOrWhiteSpace(serviceTypeId))
        {
            errors["serviceTypeId"] = ["serviceTypeId is required."];
        }
        else if (!Guid.TryParse(serviceTypeId, out serviceType))
        {
            errors["serviceTypeId"] = ["serviceTypeId must be a valid GUID."];
        }

        var day = default(DateOnly);

        if (string.IsNullOrWhiteSpace(date))
        {
            errors["date"] = ["date is required."];
        }
        else if (!TryParseDate(date, out day))
        {
            errors["date"] = [DateFormatMessage];
        }

        if (errors.Count > 0)
        {
            return Invalid(errors, httpContext);
        }

        var result = await handler.HandleAsync(id, serviceType, day, cancellationToken);

        return MapLookup(result, httpContext, rules);
    }

    private static async Task<Results<Ok<AvailabilityResponse>, ValidationProblem, ProblemHttpResult>> GetAlternativesAsync(
        Guid id,
        string? date,
        GetAlternativesHandler handler,
        SchedulingRules rules,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        DateOnly? day = null;

        if (date is not null)
        {
            if (!TryParseDate(date, out var parsed))
            {
                return Invalid(new Dictionary<string, string[]> { ["date"] = [DateFormatMessage] }, httpContext);
            }

            day = parsed;
        }

        var result = await handler.HandleAsync(id, day, cancellationToken);

        return MapLookup(result, httpContext, rules);
    }

    private const string DateFormatMessage = "date must be a calendar date in yyyy-MM-dd format, for example 2030-01-07.";

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static ValidationProblem Invalid(Dictionary<string, string[]> errors, HttpContext httpContext) =>
        TypedResults.ValidationProblem(
            errors,
            detail: "One or more fields are missing or invalid.",
            type: ProblemResults.ValidationType,
            extensions: ProblemResults.TraceExtensions(httpContext));

    private static Results<Ok<AvailabilityResponse>, ValidationProblem, ProblemHttpResult> MapLookup(
        AvailabilityLookupResult result,
        HttpContext httpContext,
        SchedulingRules rules) =>
        result switch
        {
            AvailabilityLookupResult.Available available =>
                TypedResults.Ok(AvailabilityResponse.From(available)),
            AvailabilityLookupResult.NotFound notFound =>
                ProblemResults.NotFound(httpContext, notFound.Resource, notFound.Id),
            AvailabilityLookupResult.ServiceNotOffered =>
                ProblemResults.RuleViolated(httpContext, BookingRuleViolation.ServiceNotOffered),
            AvailabilityLookupResult.ChangeRuleViolated violated =>
                ProblemResults.ChangeRuleViolated(httpContext, violated.Violation, rules.ChangeCutoff),
            _ => throw new InvalidOperationException($"Unexpected availability result '{result.GetType().Name}'."),
        };
}
