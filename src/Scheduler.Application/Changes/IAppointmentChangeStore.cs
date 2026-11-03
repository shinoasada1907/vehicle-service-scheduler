using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Changes;

public interface IAppointmentChangeStore
{
    Task<Appointment?> FindAppointmentAsync(Guid id, CancellationToken cancellationToken);

    Task<ChangeReferences> LoadReferencesAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        CancellationToken cancellationToken);

    Task<DaySchedule> LoadDayScheduleAsync(
        Guid dealershipId,
        Guid? vehicleId,
        TimeRange window,
        CancellationToken cancellationToken);

    Task<CancelOutcome> TryCancelAsync(
        Guid id,
        DateTimeOffset now,
        TimeSpan cutoff,
        CancellationToken cancellationToken);

    Task<MoveOutcome> TryMoveAsync(
        Appointment current,
        TimeRange newPeriod,
        Guid serviceBayId,
        Guid technicianId,
        DateTimeOffset now,
        TimeSpan cutoff,
        CancellationToken cancellationToken);
}

public sealed record ChangeReferences(Dealership? Dealership, ServiceType? ServiceType);

public sealed record DaySchedule(
    IReadOnlyList<ServiceBay> Bays,
    IReadOnlyList<Technician> Technicians,
    IReadOnlyList<Appointment> ConfirmedDealershipAppointments,
    IReadOnlyList<Appointment> ConfirmedVehicleAppointments);

public enum CancelOutcome
{
    Cancelled,
    NotChanged,
}

public enum MoveOutcome
{
    Moved,
    NotChanged,
    BayTaken,
    TechnicianTaken,
    VehicleTaken,
    Deadlocked,
}
