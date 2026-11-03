using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Scheduler.Infrastructure.Persistence;

internal enum ConflictingResource
{
    Bay,
    Technician,
    Vehicle,
}

internal static class AppointmentStoreSupport
{
    public static async Task LockResourcesAsync(
        SchedulerDbContext db,
        Guid serviceBayId,
        Guid technicianId,
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('bay:' || {serviceBayId}::text, 0))",
            cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('technician:' || {technicianId}::text, 0))",
            cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('vehicle:' || {vehicleId}::text, 0))",
            cancellationToken);
    }

    public static PostgresException? FindPostgresException(Exception exception, string sqlState)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException && postgresException.SqlState == sqlState)
            {
                return postgresException;
            }
        }

        return null;
    }

    public static ConflictingResource ToConflictingResource(PostgresException violation, Exception exception) =>
        violation.ConstraintName switch
        {
            AppointmentConstraints.BayNoOverlap => ConflictingResource.Bay,
            AppointmentConstraints.TechnicianNoOverlap => ConflictingResource.Technician,
            AppointmentConstraints.VehicleNoOverlap => ConflictingResource.Vehicle,
            _ => throw new InvalidOperationException(
                $"Unexpected exclusion constraint '{violation.ConstraintName}'.",
                exception),
        };
}
