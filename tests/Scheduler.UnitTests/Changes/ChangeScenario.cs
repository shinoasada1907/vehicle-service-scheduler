using Microsoft.Extensions.Time.Testing;
using Scheduler.Application.Changes;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests.Changes;

internal sealed class ChangeScenario
{
    public ChangeScenario()
    {
        var vehicle = TestData.NewVehicle(TestData.CustomerId);
        VehicleId = vehicle.Id;
        Bay1 = TestData.NewBay("Bay 1");
        Bay2 = TestData.NewBay("Bay 2");
        Alice = TestData.NewTechnician("Alice", TestData.OilChange);
        Ben = TestData.NewTechnician("Ben", TestData.OilChange);

        Store.Dealerships.Add(TestData.London());
        Store.ServiceTypes.Add(TestData.OilChange);
        Store.Bays.AddRange([Bay1, Bay2]);
        Store.Technicians.AddRange([Alice, Ben]);

        Appointment = Appointment.Confirm(
            Guid.NewGuid(),
            TestData.LondonId,
            TestData.CustomerId,
            VehicleId,
            TestData.OilChange.Id,
            Bay1.Id,
            Alice.Id,
            TimeRange.FromDuration(TestData.Utc("2030-01-07T09:00:00Z"), TimeSpan.FromHours(1)),
            TestData.Now);
        Store.Appointments.Add(Appointment);
    }

    public FakeAppointmentChangeStore Store { get; } = new();

    public FakeTimeProvider Clock { get; } = new(TestData.Now);

    public SchedulingRules Rules { get; } = SchedulingRules.Default;

    public Guid VehicleId { get; }

    public ServiceBay Bay1 { get; }

    public ServiceBay Bay2 { get; }

    public Technician Alice { get; }

    public Technician Ben { get; }

    public Appointment Appointment { get; }

    public RescheduleAppointmentHandler Reschedule() => new(Store, Store, Clock, Rules);

    public CancelAppointmentHandler Cancel() => new(Store, Store, Clock, Rules);

    public GetAlternativesHandler Alternatives() => new(Store, Clock, Rules);
}
