using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduler.Application.Booking;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Infrastructure.Persistence;

public sealed class BookingStore(SchedulerDbContext db) : IBookingStore
{
    public async Task<BookingReferences> LoadReferencesAsync(
        BookAppointmentCommand command,
        CancellationToken cancellationToken)
    {
        var dealership = await db.Dealerships
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.DealershipId, cancellationToken);

        var serviceType = await db.ServiceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ServiceTypeId, cancellationToken);

        var vehicle = await db.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.VehicleId, cancellationToken);

        var customerExists = await db.Customers
            .AnyAsync(candidate => candidate.Id == command.CustomerId, cancellationToken);

        return new BookingReferences(dealership, serviceType, vehicle, customerExists);
    }

    public async Task<ResourceSchedule> LoadScheduleAsync(
        Guid dealershipId,
        Guid vehicleId,
        TimeRange period,
        CancellationToken cancellationToken)
    {
        var start = period.Start;
        var end = period.End;

        var bays = await db.ServiceBays
            .AsNoTracking()
            .Where(bay => bay.DealershipId == dealershipId)
            .ToListAsync(cancellationToken);

        var technicians = await db.Technicians
            .AsNoTracking()
            .Where(technician => technician.DealershipId == dealershipId)
            .ToListAsync(cancellationToken);

        var dealershipAppointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.DealershipId == dealershipId
                && appointment.Status == AppointmentStatus.Confirmed
                && appointment.StartAt < end
                && appointment.EndAt > start)
            .ToListAsync(cancellationToken);

        var vehicleAppointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.VehicleId == vehicleId
                && appointment.Status == AppointmentStatus.Confirmed
                && appointment.StartAt < end
                && appointment.EndAt > start)
            .ToListAsync(cancellationToken);

        return new ResourceSchedule(bays, technicians, dealershipAppointments, vehicleAppointments);
    }

    public async Task<AddAppointmentResult> TryAddAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        db.Appointments.Add(appointment);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AddAppointmentResult.Added;
        }
        catch (Exception exception)
            when (FindPostgresException(exception, PostgresErrorCodes.DeadlockDetected) is not null)
        {
            db.ChangeTracker.Clear();
            return AddAppointmentResult.Deadlocked;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation } violation)
        {
            db.ChangeTracker.Clear();

            return violation.ConstraintName switch
            {
                AppointmentConstraints.BayNoOverlap => AddAppointmentResult.BayTaken,
                AppointmentConstraints.TechnicianNoOverlap => AddAppointmentResult.TechnicianTaken,
                AppointmentConstraints.VehicleNoOverlap => AddAppointmentResult.VehicleTaken,
                _ => throw new InvalidOperationException(
                    $"Unexpected exclusion constraint '{violation.ConstraintName}'.",
                    exception),
            };
        }
    }

    private static PostgresException? FindPostgresException(Exception exception, string sqlState)
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
}
