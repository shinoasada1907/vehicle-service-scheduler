using System.Globalization;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;

namespace Scheduler.UnitTests;

internal static class TestData
{
    public static readonly Guid CustomerId = Guid.Parse("c0000000-0000-4000-8000-000000000001");

    public static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset Utc(string iso8601) =>
        DateTimeOffset.Parse(iso8601, CultureInfo.InvariantCulture).ToUniversalTime();

    public static Dealership London() => new(
        Guid.Parse("d0000000-0000-4000-8000-000000000001"),
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
}
