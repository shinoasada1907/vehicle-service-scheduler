using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;

namespace Scheduler.Application.Appointments;

public sealed record AppointmentDetails(
    Guid Id,
    string Status,
    Guid CustomerId,
    Guid VehicleId,
    DealershipSummary Dealership,
    ServiceTypeSummary ServiceType,
    ResourceSummary ServiceBay,
    ResourceSummary Technician,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    DateTimeOffset CreatedAt)
{
    public static AppointmentDetails From(
        Appointment appointment,
        Dealership dealership,
        ServiceType serviceType,
        ServiceBay bay,
        Technician technician) => new(
            appointment.Id,
            appointment.Status.ToString(),
            appointment.CustomerId,
            appointment.VehicleId,
            new DealershipSummary(dealership.Id, dealership.Name, dealership.TimeZoneId),
            new ServiceTypeSummary(serviceType.Id, serviceType.Name, serviceType.DurationMinutes),
            new ResourceSummary(bay.Id, bay.Name),
            new ResourceSummary(technician.Id, technician.Name),
            appointment.StartAt,
            appointment.EndAt,
            appointment.CreatedAt);
}

public sealed record DealershipSummary(Guid Id, string Name, string TimeZone);

public sealed record ServiceTypeSummary(Guid Id, string Name, int DurationMinutes);

public sealed record ResourceSummary(Guid Id, string Name);
