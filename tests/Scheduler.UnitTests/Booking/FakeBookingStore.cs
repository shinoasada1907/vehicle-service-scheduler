using Scheduler.Application.Booking;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Booking;

internal sealed class FakeBookingStore : IBookingStore
{
    public List<Dealership> Dealerships { get; } = [];

    public List<ServiceType> ServiceTypes { get; } = [];

    public List<Vehicle> Vehicles { get; } = [];

    public List<Guid> CustomerIds { get; } = [];

    public List<ServiceBay> Bays { get; } = [];

    public List<Technician> Technicians { get; } = [];

    public List<Appointment> Appointments { get; } = [];

    public Queue<(AddAppointmentResult Result, Appointment? Winner)> Conflicts { get; } = new();

    public int ScheduleLoads { get; private set; }

    public Task<BookingReferences> LoadReferencesAsync(
        BookAppointmentCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BookingReferences(
            Dealerships.FirstOrDefault(dealership => dealership.Id == command.DealershipId),
            ServiceTypes.FirstOrDefault(serviceType => serviceType.Id == command.ServiceTypeId),
            Vehicles.FirstOrDefault(vehicle => vehicle.Id == command.VehicleId),
            CustomerIds.Contains(command.CustomerId)));

    public Task<ResourceSchedule> LoadScheduleAsync(
        Guid dealershipId,
        Guid vehicleId,
        TimeRange period,
        CancellationToken cancellationToken)
    {
        ScheduleLoads++;

        return Task.FromResult(new ResourceSchedule(
            Bays.Where(bay => bay.DealershipId == dealershipId).ToList(),
            Technicians.Where(technician => technician.DealershipId == dealershipId).ToList(),
            Appointments
                .Where(appointment => appointment.DealershipId == dealershipId && appointment.Period.Overlaps(period))
                .ToList(),
            Appointments
                .Where(appointment => appointment.VehicleId == vehicleId && appointment.Period.Overlaps(period))
                .ToList()));
    }

    public Task<AddAppointmentResult> TryAddAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        if (Conflicts.TryDequeue(out var conflict))
        {
            if (conflict.Winner is not null)
            {
                Appointments.Add(conflict.Winner);
            }

            return Task.FromResult(conflict.Result);
        }

        Appointments.Add(appointment);
        return Task.FromResult(AddAppointmentResult.Added);
    }
}
