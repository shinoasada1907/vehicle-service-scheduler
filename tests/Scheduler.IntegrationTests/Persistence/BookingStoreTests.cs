using Scheduler.Application.Booking;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class BookingStoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset CreatedAt = new(2029, 12, 1, 0, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync() =>
        await fixture.ResetAppointmentsAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task S8_BackToBackBookingsOnTheSameResources_AreBothAdded()
    {
        var first = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Alice,
            SeedData.Vehicles.JaneGolf,
            hour: 9);
        var second = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Alice,
            SeedData.Vehicles.JaneGolf,
            hour: 10);

        Assert.Equal(AddAppointmentResult.Added, await AddAsync(first));
        Assert.Equal(AddAppointmentResult.Added, await AddAsync(second));
    }

    [Fact]
    public async Task S29_SameBayAtOverlappingTime_IsRejectedAsBayTaken()
    {
        var first = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Alice,
            SeedData.Vehicles.JaneGolf,
            hour: 9);
        var second = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Ben,
            SeedData.Vehicles.JaneTesla,
            hour: 9,
            minute: 30);

        Assert.Equal(AddAppointmentResult.Added, await AddAsync(first));
        Assert.Equal(AddAppointmentResult.BayTaken, await AddAsync(second));
    }

    [Fact]
    public async Task S29_SameTechnicianAtOverlappingTime_IsRejectedAsTechnicianTaken()
    {
        var first = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Alice,
            SeedData.Vehicles.JaneGolf,
            hour: 9);
        var second = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay2,
            SeedData.Technicians.Alice,
            SeedData.Vehicles.JaneTesla,
            hour: 9,
            minute: 30);

        Assert.Equal(AddAppointmentResult.Added, await AddAsync(first));
        Assert.Equal(AddAppointmentResult.TechnicianTaken, await AddAsync(second));
    }

    [Fact]
    public async Task S29_SameVehicleAtOverlappingTime_IsRejectedAsVehicleTaken()
    {
        var first = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Alice,
            SeedData.Vehicles.JaneGolf,
            hour: 9);
        var second = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay2,
            SeedData.Technicians.Ben,
            SeedData.Vehicles.JaneGolf,
            hour: 9,
            minute: 30);

        Assert.Equal(AddAppointmentResult.Added, await AddAsync(first));
        Assert.Equal(AddAppointmentResult.VehicleTaken, await AddAsync(second));
    }

    [Fact]
    public async Task S30_SameVehicleAtAnotherDealershipAtOverlappingTime_IsRejectedAsVehicleTaken()
    {
        var london = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Alice,
            SeedData.Vehicles.JaneGolf,
            hour: 9);
        var saigon = Confirmed(
            SeedData.Dealerships.Saigon,
            SeedData.Bays.SaigonBayA,
            SeedData.Technicians.Minh,
            SeedData.Vehicles.JaneGolf,
            hour: 9,
            minute: 30);

        Assert.Equal(AddAppointmentResult.Added, await AddAsync(london));
        Assert.Equal(AddAppointmentResult.VehicleTaken, await AddAsync(saigon));
    }

    [Fact]
    public async Task E1_AfterARejectedInsert_TheSameStoreCanAddAnotherAppointment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateDbContext();
        var store = new BookingStore(db);

        var first = await store.TryAddAsync(
            Confirmed(
                SeedData.Dealerships.London,
                SeedData.Bays.LondonBay1,
                SeedData.Technicians.Alice,
                SeedData.Vehicles.JaneGolf,
                hour: 9),
            cancellationToken);
        var rejected = await store.TryAddAsync(
            Confirmed(
                SeedData.Dealerships.London,
                SeedData.Bays.LondonBay1,
                SeedData.Technicians.Ben,
                SeedData.Vehicles.JaneTesla,
                hour: 9),
            cancellationToken);
        var retried = await store.TryAddAsync(
            Confirmed(
                SeedData.Dealerships.London,
                SeedData.Bays.LondonBay2,
                SeedData.Technicians.Ben,
                SeedData.Vehicles.JaneTesla,
                hour: 9),
            cancellationToken);

        Assert.Equal(AddAppointmentResult.Added, first);
        Assert.Equal(AddAppointmentResult.BayTaken, rejected);
        Assert.Equal(AddAppointmentResult.Added, retried);
    }

    [Fact]
    public async Task E2_Schedule_HoldsOnlyTheDealershipsResourcesAndTheVehiclesAppointments()
    {
        var londonGolf = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Alice,
            SeedData.Vehicles.JaneGolf,
            hour: 9);
        var saigonTesla = Confirmed(
            SeedData.Dealerships.Saigon,
            SeedData.Bays.SaigonBayA,
            SeedData.Technicians.Minh,
            SeedData.Vehicles.JaneTesla,
            hour: 9);
        var londonVinfast = Confirmed(
            SeedData.Dealerships.London,
            SeedData.Bays.LondonBay2,
            SeedData.Technicians.Ben,
            SeedData.Vehicles.AnVinfast,
            hour: 11);
        Assert.Equal(AddAppointmentResult.Added, await AddAsync(londonGolf));
        Assert.Equal(AddAppointmentResult.Added, await AddAsync(saigonTesla));
        Assert.Equal(AddAppointmentResult.Added, await AddAsync(londonVinfast));
        var period = new TimeRange(Utc(hour: 9), Utc(hour: 10));

        await using var db = fixture.CreateDbContext();
        var schedule = await new BookingStore(db).LoadScheduleAsync(
            SeedData.Dealerships.London,
            SeedData.Vehicles.JaneTesla,
            period,
            TestContext.Current.CancellationToken);

        Assert.Equal(4, schedule.Bays.Count);
        Assert.All(schedule.Bays, bay => Assert.Equal(SeedData.Dealerships.London, bay.DealershipId));
        Assert.Equal(3, schedule.Technicians.Count);
        Assert.All(schedule.Technicians, technician => Assert.Equal(SeedData.Dealerships.London, technician.DealershipId));
        Assert.Equal(londonGolf.Id, Assert.Single(schedule.ConfirmedDealershipAppointments).Id);
        Assert.Equal(saigonTesla.Id, Assert.Single(schedule.ConfirmedVehicleAppointments).Id);
    }

    private static DateTimeOffset Utc(int hour, int minute = 0) => new(2030, 1, 7, hour, minute, 0, TimeSpan.Zero);

    private static Appointment Confirmed(Guid dealershipId, Guid bayId, Guid technicianId, Guid vehicleId, int hour, int minute = 0)
    {
        var customerId = vehicleId == SeedData.Vehicles.AnVinfast
            ? SeedData.Customers.NguyenVanAn
            : SeedData.Customers.JaneSmith;

        return Appointment.Confirm(
            Guid.CreateVersion7(),
            dealershipId,
            customerId,
            vehicleId,
            SeedData.ServiceTypes.OilChange,
            bayId,
            technicianId,
            TimeRange.FromDuration(Utc(hour, minute), TimeSpan.FromMinutes(60)),
            CreatedAt);
    }

    private async Task<AddAppointmentResult> AddAsync(Appointment appointment)
    {
        await using var db = fixture.CreateDbContext();
        return await new BookingStore(db).TryAddAsync(appointment, TestContext.Current.CancellationToken);
    }
}
