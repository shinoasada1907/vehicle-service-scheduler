using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Booking;

public interface IBookingStore
{
    Task<BookingReferences> LoadReferencesAsync(BookAppointmentCommand command, CancellationToken cancellationToken);

    Task<ResourceSchedule> LoadScheduleAsync(
        Guid dealershipId,
        Guid vehicleId,
        TimeRange period,
        CancellationToken cancellationToken);

    Task<AddAppointmentResult> TryAddAsync(Appointment appointment, CancellationToken cancellationToken);
}

public sealed record BookingReferences(
    Dealership? Dealership,
    ServiceType? ServiceType,
    Vehicle? Vehicle,
    bool CustomerExists);

public sealed record ResourceSchedule(
    IReadOnlyList<ServiceBay> Bays,
    IReadOnlyList<Technician> Technicians,
    IReadOnlyList<Appointment> ConfirmedDealershipAppointments,
    IReadOnlyList<Appointment> ConfirmedVehicleAppointments);

public enum AddAppointmentResult
{
    Added,
    BayTaken,
    TechnicianTaken,
    VehicleTaken,
    Deadlocked,
}
