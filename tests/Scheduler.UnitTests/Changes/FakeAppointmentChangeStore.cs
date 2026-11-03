using Scheduler.Application.Appointments;
using Scheduler.Application.Changes;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Changes;

internal sealed class FakeAppointmentChangeStore : IAppointmentChangeStore, IAppointmentQueries
{
    public List<Dealership> Dealerships { get; } = [];

    public List<ServiceType> ServiceTypes { get; } = [];

    public List<ServiceBay> Bays { get; } = [];

    public List<Technician> Technicians { get; } = [];

    public List<Appointment> Appointments { get; } = [];

    public Queue<MoveOutcome> ScriptedMoves { get; } = new();

    public Queue<CancelOutcome> ScriptedCancels { get; } = new();

    public List<(Guid AppointmentId, TimeRange Period)> Moves { get; } = [];

    public int ScheduleLoads { get; private set; }

    public Task<Appointment?> FindAppointmentAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Appointments.FirstOrDefault(appointment => appointment.Id == id));

    public Task<ChangeReferences> LoadReferencesAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ChangeReferences(
            Dealerships.FirstOrDefault(dealership => dealership.Id == dealershipId),
            ServiceTypes.FirstOrDefault(serviceType => serviceType.Id == serviceTypeId)));

    public Task<DaySchedule> LoadDayScheduleAsync(
        Guid dealershipId,
        Guid? vehicleId,
        TimeRange window,
        CancellationToken cancellationToken)
    {
        ScheduleLoads++;

        var confirmed = Appointments
            .Where(appointment => appointment.Status == AppointmentStatus.Confirmed
                && appointment.Period.Overlaps(window))
            .ToList();

        return Task.FromResult(new DaySchedule(
            Bays.Where(bay => bay.DealershipId == dealershipId).ToList(),
            Technicians.Where(technician => technician.DealershipId == dealershipId).ToList(),
            confirmed.Where(appointment => appointment.DealershipId == dealershipId).ToList(),
            confirmed.Where(appointment => appointment.VehicleId == vehicleId).ToList()));
    }

    public Task<CancelOutcome> TryCancelAsync(
        Guid id,
        DateTimeOffset now,
        TimeSpan cutoff,
        CancellationToken cancellationToken)
    {
        if (ScriptedCancels.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }

        var appointment = Appointments.FirstOrDefault(candidate => candidate.Id == id);

        if (appointment is null
            || appointment.Status != AppointmentStatus.Confirmed
            || appointment.StartAt - now <= cutoff)
        {
            return Task.FromResult(CancelOutcome.NotChanged);
        }

        appointment.Cancel(now);
        return Task.FromResult(CancelOutcome.Cancelled);
    }

    public Task<MoveOutcome> TryMoveAsync(
        Appointment current,
        TimeRange newPeriod,
        Guid serviceBayId,
        Guid technicianId,
        DateTimeOffset now,
        TimeSpan cutoff,
        CancellationToken cancellationToken)
    {
        if (ScriptedMoves.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }

        Moves.Add((current.Id, newPeriod));
        current.Reschedule(newPeriod, serviceBayId, technicianId);
        return Task.FromResult(MoveOutcome.Moved);
    }

    public Task<AppointmentDetails?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var appointment = Appointments.FirstOrDefault(candidate => candidate.Id == id);

        if (appointment is null)
        {
            return Task.FromResult<AppointmentDetails?>(null);
        }

        return Task.FromResult<AppointmentDetails?>(AppointmentDetails.From(
            appointment,
            Dealerships.Single(dealership => dealership.Id == appointment.DealershipId),
            ServiceTypes.Single(serviceType => serviceType.Id == appointment.ServiceTypeId),
            Bays.Single(bay => bay.Id == appointment.ServiceBayId),
            Technicians.Single(technician => technician.Id == appointment.TechnicianId)));
    }
}
