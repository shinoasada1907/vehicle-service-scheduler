using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Catalog;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Dealerships;
using Scheduler.Domain.Resources;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Seeding;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(SchedulerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Dealerships.AnyAsync(cancellationToken))
        {
            return;
        }

        DayOfWeek[] weekdays =
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
        ];

        var london = new Dealership(
            SeedData.Dealerships.London,
            "Riverside Motors London",
            "Europe/London",
            [
                .. weekdays.Select(day => new OpeningHours(day, new TimeOnly(8, 0), new TimeOnly(18, 0))),
                new OpeningHours(DayOfWeek.Saturday, new TimeOnly(9, 0), new TimeOnly(13, 0)),
            ]);

        var saigon = new Dealership(
            SeedData.Dealerships.Saigon,
            "Saigon Auto Service",
            "Asia/Ho_Chi_Minh",
            [.. weekdays.Append(DayOfWeek.Saturday).Select(day => new OpeningHours(day, new TimeOnly(8, 0), new TimeOnly(17, 0)))]);

        ServiceType[] serviceTypes =
        [
            new(SeedData.ServiceTypes.OilChange, "Oil change", 60, BayType.General),
            new(SeedData.ServiceTypes.BrakeInspection, "Brake inspection", 90, BayType.General),
            new(SeedData.ServiceTypes.WheelAlignment, "Wheel alignment", 60, BayType.Alignment),
            new(SeedData.ServiceTypes.EvBatteryCheck, "EV battery check", 120, BayType.ElectricVehicle),
        ];

        ServiceBay[] bays =
        [
            new(SeedData.Bays.LondonBay1, SeedData.Dealerships.London, "London Bay 1", BayType.General),
            new(SeedData.Bays.LondonBay2, SeedData.Dealerships.London, "London Bay 2", BayType.General),
            new(SeedData.Bays.LondonAlignmentBay, SeedData.Dealerships.London, "London Alignment Bay", BayType.Alignment),
            new(SeedData.Bays.LondonEvBay, SeedData.Dealerships.London, "London EV Bay", BayType.ElectricVehicle),
            new(SeedData.Bays.SaigonBayA, SeedData.Dealerships.Saigon, "Saigon Bay A", BayType.General),
            new(SeedData.Bays.SaigonBayB, SeedData.Dealerships.Saigon, "Saigon Bay B", BayType.General),
        ];

        Technician[] technicians =
        [
            new(
                SeedData.Technicians.Alice,
                SeedData.Dealerships.London,
                "Alice Walker",
                [SeedData.ServiceTypes.OilChange, SeedData.ServiceTypes.BrakeInspection, SeedData.ServiceTypes.WheelAlignment]),
            new(
                SeedData.Technicians.Ben,
                SeedData.Dealerships.London,
                "Ben Carter",
                [SeedData.ServiceTypes.OilChange, SeedData.ServiceTypes.BrakeInspection]),
            new(
                SeedData.Technicians.Chloe,
                SeedData.Dealerships.London,
                "Chloe Evans",
                [SeedData.ServiceTypes.OilChange, SeedData.ServiceTypes.EvBatteryCheck]),
            new(
                SeedData.Technicians.Minh,
                SeedData.Dealerships.Saigon,
                "Tran Van Minh",
                [SeedData.ServiceTypes.OilChange, SeedData.ServiceTypes.BrakeInspection]),
            new(
                SeedData.Technicians.Lan,
                SeedData.Dealerships.Saigon,
                "Le Thi Lan",
                [SeedData.ServiceTypes.OilChange]),
        ];

        Customer[] customers =
        [
            new(SeedData.Customers.JaneSmith, "Jane Smith", "jane.smith@example.com"),
            new(SeedData.Customers.NguyenVanAn, "Nguyen Van An", "an.nguyen@example.com"),
        ];

        Vehicle[] vehicles =
        [
            new(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, "WVWZZZ1KZ6W000001", "Volkswagen", "Golf", 2021),
            new(SeedData.Vehicles.JaneTesla, SeedData.Customers.JaneSmith, "5YJ3E1EA7KF000002", "Tesla", "Model 3", 2023),
            new(SeedData.Vehicles.AnVinfast, SeedData.Customers.NguyenVanAn, "RLLV3CAA5PV000003", "VinFast", "VF 8", 2024),
        ];

        db.Dealerships.AddRange(london, saigon);
        db.ServiceTypes.AddRange(serviceTypes);
        db.ServiceBays.AddRange(bays);
        db.Technicians.AddRange(technicians);
        db.Customers.AddRange(customers);
        db.Vehicles.AddRange(vehicles);

        await db.SaveChangesAsync(cancellationToken);
    }
}
