using Microsoft.EntityFrameworkCore;
using Npgsql;
using Scheduler.Application.Changes;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Infrastructure.Persistence;

public sealed class AppointmentChangeStore(SchedulerDbContext db) : IAppointmentChangeStore
{
    public Task<Appointment?> FindAppointmentAsync(Guid id, CancellationToken cancellationToken) =>
        db.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

    public async Task<ChangeReferences> LoadReferencesAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        CancellationToken cancellationToken)
    {
        var dealership = await db.Dealerships
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == dealershipId, cancellationToken);

        var serviceType = await db.ServiceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == serviceTypeId, cancellationToken);

        return new ChangeReferences(dealership, serviceType);
    }

    public async Task<DaySchedule> LoadDayScheduleAsync(
        Guid dealershipId,
        Guid? vehicleId,
        TimeRange window,
        CancellationToken cancellationToken)
    {
        var start = window.Start;
        var end = window.End;

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

        var vehicleAppointments = vehicleId is null
            ? []
            : await db.Appointments
                .AsNoTracking()
                .Where(appointment => appointment.VehicleId == vehicleId
                    && appointment.Status == AppointmentStatus.Confirmed
                    && appointment.StartAt < end
                    && appointment.EndAt > start)
                .ToListAsync(cancellationToken);

        return new DaySchedule(bays, technicians, dealershipAppointments, vehicleAppointments);
    }

    public async Task<CancelOutcome> TryCancelAsync(
        Guid id,
        DateTimeOffset now,
        TimeSpan cutoff,
        CancellationToken cancellationToken)
    {
        var earliestAllowedStart = now + cutoff;

        var rows = await db.Appointments
            .Where(appointment => appointment.Id == id
                && appointment.Status == AppointmentStatus.Confirmed
                && appointment.StartAt > earliestAllowedStart)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(appointment => appointment.Status, AppointmentStatus.Cancelled)
                    .SetProperty(appointment => appointment.CancelledAt, (DateTimeOffset?)now),
                cancellationToken);

        return rows == 1 ? CancelOutcome.Cancelled : CancelOutcome.NotChanged;
    }

    public async Task<MoveOutcome> TryMoveAsync(
        Appointment current,
        TimeRange newPeriod,
        Guid serviceBayId,
        Guid technicianId,
        DateTimeOffset now,
        TimeSpan cutoff,
        CancellationToken cancellationToken)
    {
        var earliestAllowedStart = now + cutoff;
        var id = current.Id;
        var oldStart = current.StartAt;
        var newStart = newPeriod.Start;
        var newEnd = newPeriod.End;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await AppointmentStoreSupport.LockResourcesAsync(
                db,
                serviceBayId,
                technicianId,
                current.VehicleId,
                cancellationToken);

            var rows = await db.Appointments
                .Where(appointment => appointment.Id == id
                    && appointment.Status == AppointmentStatus.Confirmed
                    && appointment.StartAt == oldStart
                    && appointment.StartAt > earliestAllowedStart)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(appointment => appointment.StartAt, newStart)
                        .SetProperty(appointment => appointment.EndAt, newEnd)
                        .SetProperty(appointment => appointment.ServiceBayId, serviceBayId)
                        .SetProperty(appointment => appointment.TechnicianId, technicianId),
                    cancellationToken);

            if (rows == 0)
            {
                return MoveOutcome.NotChanged;
            }

            await transaction.CommitAsync(cancellationToken);
            return MoveOutcome.Moved;
        }
        catch (Exception exception)
            when (AppointmentStoreSupport.FindPostgresException(exception, PostgresErrorCodes.DeadlockDetected) is not null)
        {
            return MoveOutcome.Deadlocked;
        }
        catch (Exception exception)
            when (AppointmentStoreSupport.FindPostgresException(exception, PostgresErrorCodes.ExclusionViolation) is { } violation)
        {
            return AppointmentStoreSupport.ToConflictingResource(violation, exception) switch
            {
                ConflictingResource.Bay => MoveOutcome.BayTaken,
                ConflictingResource.Technician => MoveOutcome.TechnicianTaken,
                _ => MoveOutcome.VehicleTaken,
            };
        }
    }
}
