using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Npgsql;
using OpenTelemetry;
using Scalar.AspNetCore;
using Scheduler.Api.Appointments;
using Scheduler.Api.Problems;
using Scheduler.Application;
using Scheduler.Application.Booking;
using Scheduler.Application.Changes;
using Scheduler.Domain.Scheduling;
using Scheduler.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
});

var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        builder.Configuration["OTEL_SERVICE_NAME"] ?? "scheduler-api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options => options.Filter = IsBusinessRequest)
        .AddNpgsql()
        .AddSource(SchedulerTelemetry.Name))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddNpgsqlInstrumentation()
        .AddMeter(SchedulerTelemetry.Name));

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    telemetry.UseOtlpExporter();
}

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemResults.Normalize);
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure(services =>
    services.GetRequiredService<IConfiguration>().GetConnectionString("Scheduler")
    ?? throw new InvalidOperationException("Connection string 'Scheduler' is not configured."));
builder.Services.AddSingleton(services => ReadSchedulingRules(services.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<BookAppointmentHandler>();
builder.Services.AddScoped<GetAvailabilityHandler>();
builder.Services.AddScoped<GetAlternativesHandler>();
builder.Services.AddScoped<RescheduleAppointmentHandler>();
builder.Services.AddScoped<CancelAppointmentHandler>();

var app = builder.Build();

_ = app.Services.GetRequiredService<SchedulingRules>();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .ExcludeFromDescription();
app.MapHealthChecks(
        "/health/ready",
        new HealthCheckOptions { Predicate = check => check.Tags.Contains(DependencyInjection.ReadyHealthTag) })
    .ExcludeFromDescription();
app.MapAppointmentEndpoints();
app.MapAvailabilityEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateAndSeedAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();

static bool IsBusinessRequest(HttpContext context)
{
    var path = context.Request.Path;
    return !path.StartsWithSegments("/health")
        && !path.StartsWithSegments("/scalar")
        && !path.StartsWithSegments("/openapi");
}

static SchedulingRules ReadSchedulingRules(IConfiguration configuration)
{
    var cutoff = configuration.GetValue<TimeSpan?>("Scheduling:ChangeCutoff");

    if (cutoff is null)
    {
        return SchedulingRules.Default;
    }

    if (cutoff <= TimeSpan.Zero)
    {
        throw new InvalidOperationException(
            $"Configuration 'Scheduling:ChangeCutoff' must be greater than zero, but was '{cutoff}'.");
    }

    return new SchedulingRules(cutoff.Value);
}

public partial class Program;
