using Scheduler.Domain.Scheduling;

namespace Scheduler.Domain.Appointments;

public sealed class Appointment
{
    private Appointment()
    {
    }

    public Guid Id { get; private set; }

    public Guid DealershipId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid VehicleId { get; private set; }

    public Guid ServiceTypeId { get; private set; }

    public Guid ServiceBayId { get; private set; }

    public Guid TechnicianId { get; private set; }

    public DateTimeOffset StartAt { get; private set; }

    public DateTimeOffset EndAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public TimeRange Period => new(StartAt, EndAt);

    public static Appointment Confirm(
        Guid id,
        Guid dealershipId,
        Guid customerId,
        Guid vehicleId,
        Guid serviceTypeId,
        Guid serviceBayId,
        Guid technicianId,
        TimeRange period,
        DateTimeOffset createdAt)
    {
        return new Appointment
        {
            Id = id,
            DealershipId = dealershipId,
            CustomerId = customerId,
            VehicleId = vehicleId,
            ServiceTypeId = serviceTypeId,
            ServiceBayId = serviceBayId,
            TechnicianId = technicianId,
            StartAt = period.Start,
            EndAt = period.End,
            Status = AppointmentStatus.Confirmed,
            CreatedAt = createdAt,
        };
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == AppointmentStatus.Cancelled)
        {
            return;
        }

        Status = AppointmentStatus.Cancelled;
        CancelledAt = now;
    }

    public void Reschedule(TimeRange period, Guid serviceBayId, Guid technicianId)
    {
        StartAt = period.Start;
        EndAt = period.End;
        ServiceBayId = serviceBayId;
        TechnicianId = technicianId;
    }
}
