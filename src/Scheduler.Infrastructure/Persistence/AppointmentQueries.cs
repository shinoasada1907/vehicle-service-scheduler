using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Appointments;

namespace Scheduler.Infrastructure.Persistence;

public sealed class AppointmentQueries(SchedulerDbContext db) : IAppointmentQueries
{
    public async Task<AppointmentDetails?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await (
            from appointment in db.Appointments.AsNoTracking()
            join dealership in db.Dealerships on appointment.DealershipId equals dealership.Id
            join serviceType in db.ServiceTypes on appointment.ServiceTypeId equals serviceType.Id
            join bay in db.ServiceBays on appointment.ServiceBayId equals bay.Id
            join technician in db.Technicians on appointment.TechnicianId equals technician.Id
            where appointment.Id == id
            select new
            {
                Appointment = appointment,
                DealershipName = dealership.Name,
                dealership.TimeZoneId,
                ServiceTypeName = serviceType.Name,
                serviceType.DurationMinutes,
                BayName = bay.Name,
                TechnicianName = technician.Name,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var appointmentRow = row.Appointment;

        return new AppointmentDetails(
            appointmentRow.Id,
            appointmentRow.Status.ToString(),
            appointmentRow.CustomerId,
            appointmentRow.VehicleId,
            new DealershipSummary(appointmentRow.DealershipId, row.DealershipName, row.TimeZoneId),
            new ServiceTypeSummary(appointmentRow.ServiceTypeId, row.ServiceTypeName, row.DurationMinutes),
            new ResourceSummary(appointmentRow.ServiceBayId, row.BayName),
            new ResourceSummary(appointmentRow.TechnicianId, row.TechnicianName),
            appointmentRow.StartAt,
            appointmentRow.EndAt,
            appointmentRow.CreatedAt);
    }
}
