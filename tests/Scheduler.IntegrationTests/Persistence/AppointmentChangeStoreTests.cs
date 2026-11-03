using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Booking;
using Scheduler.Application.Changes;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.IntegrationTests.Persistence;

[Collection(PostgresCollection.Name)]
public sealed class AppointmentChangeStoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset CreatedAt = new(2029, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Cutoff = TimeSpan.FromHours(2);

    public async ValueTask InitializeAsync() =>
        await fixture.ResetAppointmentsAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task C3_CancelTwice_SecondCallChangesNothing()
    {
        var appointment = Confirmed(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, hour: 9);
        Assert.Equal(AddAppointmentResult.Added, await AddAsync(appointment));
        var firstCancelAt = Now;
        var secondCancelAt = Now.AddMinutes(5);

        var first = await CancelAsync(appointment.Id, firstCancelAt);
        var second = await CancelAsync(appointment.Id, secondCancelAt);

        Assert.Equal(CancelOutcome.Cancelled, first);
        Assert.Equal(CancelOutcome.NotChanged, second);
        var stored = await ReadAsync(appointment.Id);
        Assert.Equal(AppointmentStatus.Cancelled, stored.Status);
        Assert.Equal(firstCancelAt, stored.CancelledAt);
    }

    [Fact]
    public async Task V3_CancelledAppointment_DoesNotBlockANewBooking()
    {
        var original = Confirmed(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, hour: 9);
        Assert.Equal(AddAppointmentResult.Added, await AddAsync(original));
        Assert.Equal(CancelOutcome.Cancelled, await CancelAsync(original.Id, Now));

        var replacement = Confirmed(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, hour: 9);

        Assert.Equal(AddAppointmentResult.Added, await AddAsync(replacement));
    }

    [Fact]
    public async Task R3_MoveRejectedByTheDatabase_LeavesTheAppointmentUnchanged()
    {
        var a = Confirmed(SeedData.Bays.LondonBay1, SeedData.Technicians.Alice, hour: 9);
        var b = Confirmed(
            SeedData.Bays.LondonBay2,
            SeedData.Technicians.Ben,
            hour: 12,
            vehicleId: SeedData.Vehicles.JaneTesla);
        Assert.Equal(AddAppointmentResult.Added, await AddAsync(a));
        Assert.Equal(AddAppointmentResult.Added, await AddAsync(b));

        MoveOutcome outcome;
        await using (var db = fixture.CreateDbContext())
        {
            outcome = await new AppointmentChangeStore(db).TryMoveAsync(
                a,
                b.Period,
                SeedData.Bays.LondonBay2,
                SeedData.Technicians.Alice,
                Now,
                Cutoff,
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(MoveOutcome.BayTaken, outcome);
        var stored = await ReadAsync(a.Id);
        Assert.Equal(a.StartAt, stored.StartAt);
        Assert.Equal(a.EndAt, stored.EndAt);
        Assert.Equal(SeedData.Bays.LondonBay1, stored.ServiceBayId);
        Assert.Equal(SeedData.Technicians.Alice, stored.TechnicianId);
    }

    private static DateTimeOffset Utc(int hour) => new(2030, 1, 7, hour, 0, 0, TimeSpan.Zero);

    private static Appointment Confirmed(Guid bayId, Guid technicianId, int hour, Guid? vehicleId = null) =>
        Appointment.Confirm(
            Guid.CreateVersion7(),
            SeedData.Dealerships.London,
            SeedData.Customers.JaneSmith,
            vehicleId ?? SeedData.Vehicles.JaneGolf,
            SeedData.ServiceTypes.OilChange,
            bayId,
            technicianId,
            TimeRange.FromDuration(Utc(hour), TimeSpan.FromMinutes(60)),
            CreatedAt);

    private async Task<AddAppointmentResult> AddAsync(Appointment appointment)
    {
        await using var db = fixture.CreateDbContext();
        return await new BookingStore(db).TryAddAsync(appointment, TestContext.Current.CancellationToken);
    }

    private async Task<CancelOutcome> CancelAsync(Guid id, DateTimeOffset now)
    {
        await using var db = fixture.CreateDbContext();
        return await new AppointmentChangeStore(db).TryCancelAsync(
            id,
            now,
            Cutoff,
            TestContext.Current.CancellationToken);
    }

    private async Task<Appointment> ReadAsync(Guid id)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Appointments
            .AsNoTracking()
            .SingleAsync(appointment => appointment.Id == id, TestContext.Current.CancellationToken);
    }
}
