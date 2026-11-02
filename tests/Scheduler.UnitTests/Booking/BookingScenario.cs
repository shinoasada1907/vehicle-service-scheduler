using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Scheduler.Application.Booking;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Resources;

namespace Scheduler.UnitTests.Booking;

internal sealed class BookingScenario
{
    public BookingScenario()
    {
        Vehicle = TestData.NewVehicle(TestData.CustomerId);
        Bay1 = TestData.NewBay("Bay 1");
        Bay2 = TestData.NewBay("Bay 2");
        Alice = TestData.NewTechnician("Alice", TestData.OilChange, TestData.BrakeInspection);
        Ben = TestData.NewTechnician("Ben", TestData.OilChange, TestData.BrakeInspection);

        Store.Dealerships.Add(TestData.London());
        Store.ServiceTypes.AddRange([TestData.OilChange, TestData.BrakeInspection]);
        Store.Vehicles.Add(Vehicle);
        Store.CustomerIds.Add(TestData.CustomerId);
        Store.Bays.AddRange([Bay1, Bay2]);
        Store.Technicians.AddRange([Alice, Ben]);
    }

    public FakeBookingStore Store { get; } = new();

    public FakeTimeProvider Clock { get; } = new(TestData.Now);

    public Vehicle Vehicle { get; }

    public ServiceBay Bay1 { get; }

    public ServiceBay Bay2 { get; }

    public Technician Alice { get; }

    public Technician Ben { get; }

    public BookAppointmentHandler Handler() => new(Store, Clock);

    public BookAppointmentCommand Command(string startAt = "2030-01-07T09:00:00Z", ServiceType? serviceType = null) =>
        new(
            TestData.CustomerId,
            Vehicle.Id,
            TestData.LondonId,
            (serviceType ?? TestData.OilChange).Id,
            DateTimeOffset.Parse(startAt, CultureInfo.InvariantCulture));
}
