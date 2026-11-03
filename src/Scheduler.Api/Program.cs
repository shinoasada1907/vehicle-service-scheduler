using Scalar.AspNetCore;
using Scheduler.Api.Appointments;
using Scheduler.Api.Problems;
using Scheduler.Application.Booking;
using Scheduler.Application.Changes;
using Scheduler.Domain.Scheduling;
using Scheduler.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

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
app.MapAppointmentEndpoints();
app.MapAvailabilityEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateAndSeedAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();

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
