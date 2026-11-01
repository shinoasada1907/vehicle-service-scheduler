using System.Globalization;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Domain.Scheduling;

namespace Scheduler.UnitTests;

internal static class TestData
{
    public static readonly Guid CustomerId = Guid.Parse("c0000000-0000-4000-8000-000000000001");

    public static readonly Guid LondonId = Guid.Parse("d0000000-0000-4000-8000-000000000001");

    public static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly ServiceType OilChange = new(
        Guid.Parse("5e000000-0000-4000-8000-000000000001"), "Oil change", 60, BayType.General);

    public static readonly ServiceType BrakeInspection = new(
        Guid.Parse("5e000000-0000-4000-8000-000000000002"), "Brake inspection", 90, BayType.General);

    public static readonly ServiceType WheelAlignment = new(
        Guid.Parse("5e000000-0000-4000-8000-000000000003"), "Wheel alignment", 60, BayType.Alignment);

    public static DateTimeOffset Utc(string iso8601) =>
        DateTimeOffset.Parse(iso8601, CultureInfo.InvariantCulture).ToUniversalTime();

    public static Dealership London() => new(
        LondonId,
        "Riverside Motors London",
        "Europe/London",
        [
            new OpeningHours(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
            new OpeningHours(DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
            new OpeningHours(DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
            new OpeningHours(DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
            new OpeningHours(DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(18, 0)),
            new OpeningHours(DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(13, 0)),
        ]);

    public static Vehicle NewVehicle(Guid customerId) => new(
        Guid.Parse("e0000000-0000-4000-8000-000000000001"),
        customerId,
        "WVWZZZ1JZXW000001",
        "Volkswagen",
        "Golf",
        2021);

    public static ServiceBay NewBay(string name, BayType bayType = BayType.General) =>
        new(Guid.NewGuid(), LondonId, name, bayType);

    public static Technician NewTechnician(string name, params ServiceType[] qualifiedFor) =>
        new(Guid.NewGuid(), LondonId, name, qualifiedFor.Select(serviceType => serviceType.Id));

    public static Appointment Booked(ServiceBay bay, Technician technician, string startUtc, int minutes = 60) =>
        Appointment.Confirm(
            Guid.NewGuid(),
            LondonId,
            CustomerId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            bay.Id,
            technician.Id,
            TimeRange.FromDuration(Utc(startUtc), TimeSpan.FromMinutes(minutes)),
            Now);
}
