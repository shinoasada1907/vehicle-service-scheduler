using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Appointments;
using Scheduler.Application.Booking;
using Scheduler.Application.Changes;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString)
    {
        services.AddDbContext<SchedulerDbContext>((provider, options) =>
            options.UseNpgsql(connectionString(provider)));
        services.AddScoped<IBookingStore, BookingStore>();
        services.AddScoped<IAppointmentChangeStore, AppointmentChangeStore>();
        services.AddScoped<IAppointmentQueries, AppointmentQueries>();

        services.AddHealthChecks()
            .AddDbContextCheck<SchedulerDbContext>(tags: [ReadyHealthTag]);

        return services;
    }

    public const string ReadyHealthTag = "ready";

    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();

        await db.Database.MigrateAsync(cancellationToken);
        await DatabaseSeeder.SeedAsync(db, cancellationToken);
    }
}
