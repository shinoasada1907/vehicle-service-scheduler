using Scalar.AspNetCore;
using Scheduler.Api.Appointments;
using Scheduler.Application.Booking;
using Scheduler.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure(services =>
    services.GetRequiredService<IConfiguration>().GetConnectionString("Scheduler")
    ?? throw new InvalidOperationException("Connection string 'Scheduler' is not configured."));
builder.Services.AddScoped<BookAppointmentHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapAppointmentEndpoints();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateAndSeedAsync(app.Lifetime.ApplicationStopping);
}

await app.RunAsync();

public partial class Program;
