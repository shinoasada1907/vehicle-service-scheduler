namespace Scheduler.Infrastructure.Seeding;

public static class SeedData
{
    public static class Dealerships
    {
        public static readonly Guid London = new("d0000000-0000-4000-8000-000000000001");

        public static readonly Guid Saigon = new("d0000000-0000-4000-8000-000000000002");
    }

    public static class ServiceTypes
    {
        public static readonly Guid OilChange = new("5e000000-0000-4000-8000-000000000001");

        public static readonly Guid BrakeInspection = new("5e000000-0000-4000-8000-000000000002");

        public static readonly Guid WheelAlignment = new("5e000000-0000-4000-8000-000000000003");

        public static readonly Guid EvBatteryCheck = new("5e000000-0000-4000-8000-000000000004");
    }

    public static class Bays
    {
        public static readonly Guid LondonBay1 = new("ba000000-0000-4000-8000-000000000001");

        public static readonly Guid LondonBay2 = new("ba000000-0000-4000-8000-000000000002");

        public static readonly Guid LondonAlignmentBay = new("ba000000-0000-4000-8000-000000000003");

        public static readonly Guid LondonEvBay = new("ba000000-0000-4000-8000-000000000004");

        public static readonly Guid SaigonBayA = new("ba000000-0000-4000-8000-000000000005");

        public static readonly Guid SaigonBayB = new("ba000000-0000-4000-8000-000000000006");
    }

    public static class Technicians
    {
        public static readonly Guid Alice = new("7e000000-0000-4000-8000-000000000001");

        public static readonly Guid Ben = new("7e000000-0000-4000-8000-000000000002");

        public static readonly Guid Chloe = new("7e000000-0000-4000-8000-000000000003");

        public static readonly Guid Minh = new("7e000000-0000-4000-8000-000000000004");

        public static readonly Guid Lan = new("7e000000-0000-4000-8000-000000000005");
    }

    public static class Customers
    {
        public static readonly Guid JaneSmith = new("c0000000-0000-4000-8000-000000000001");

        public static readonly Guid NguyenVanAn = new("c0000000-0000-4000-8000-000000000002");
    }

    public static class Vehicles
    {
        public static readonly Guid JaneGolf = new("ca000000-0000-4000-8000-000000000001");

        public static readonly Guid JaneTesla = new("ca000000-0000-4000-8000-000000000002");

        public static readonly Guid AnVinfast = new("ca000000-0000-4000-8000-000000000003");
    }
}
