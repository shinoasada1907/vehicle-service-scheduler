namespace Scheduler.Application.Appointments;

public interface IAppointmentQueries
{
    Task<AppointmentDetails?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
