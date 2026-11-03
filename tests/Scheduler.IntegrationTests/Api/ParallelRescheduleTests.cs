using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Customers;
using Scheduler.Domain.Scheduling;
using Scheduler.Infrastructure.Seeding;
using static Scheduler.IntegrationTests.Api.ApiTestHelpers;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class ParallelRescheduleTests : IAsyncLifetime
{
    private const string MondayAt1100Utc = "2030-01-07T11:00:00Z";
    private const int Rounds = 10;

    private readonly PostgresFixture _fixture;
    private readonly SchedulerApiFactory _factory;
    private readonly HttpClient _client;

    public ParallelRescheduleTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _factory = new SchedulerApiFactory(fixture);
        _client = _factory.CreateClient();
    }

    public async ValueTask InitializeAsync() =>
        await _fixture.ResetAppointmentsAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task R10_RescheduleAndBookingRaceForTheLastFreeTime_OnlyOneWins()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var nineToTen = new TimeRange(
            new DateTimeOffset(2030, 1, 7, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 7, 10, 0, 0, TimeSpan.Zero));

        for (var round = 0; round < Rounds; round++)
        {
            await _fixture.ResetAppointmentsAsync(cancellationToken);

            var vehicleIds = await CreateVehiclesAsync(3, cancellationToken);
            await BookAsync(vehicleIds[0], MondayAt0900Utc);
            var rescheduledId = await BookAsync(vehicleIds[1], MondayAt1100Utc);

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var rescheduleTask = Task.Run(async () =>
            {
                await gate.Task;
                return await _client.PostAsJsonAsync(
                    $"/appointments/{rescheduledId}/reschedule",
                    new { startAt = MondayAt0900Utc },
                    cancellationToken);
            }, cancellationToken);

            var bookingTask = Task.Run(async () =>
            {
                await gate.Task;
                return await _client.PostBookingAsync(BookingRequest(
                    SeedData.Customers.JaneSmith,
                    vehicleIds[2],
                    SeedData.Dealerships.London,
                    SeedData.ServiceTypes.OilChange,
                    MondayAt0900Utc));
            }, cancellationToken);

            gate.SetResult();
            var responses = await Task.WhenAll(rescheduleTask, bookingTask);
            var reschedule = responses[0];
            var booking = responses[1];

            try
            {
                var rescheduleWon = reschedule.StatusCode == HttpStatusCode.OK;
                var bookingWon = booking.StatusCode == HttpStatusCode.Created;

                Assert.True(rescheduleWon ^ bookingWon, $"Round {round}: exactly one request must win.");

                await AssertProblemAsync(rescheduleWon ? booking : reschedule, HttpStatusCode.Conflict, "no-capacity");
            }
            finally
            {
                reschedule.Dispose();
                booking.Dispose();
            }

            await using var verificationDb = _fixture.CreateDbContext();
            var confirmed = await verificationDb.Appointments
                .AsNoTracking()
                .Where(appointment => appointment.Status == AppointmentStatus.Confirmed)
                .ToListAsync(cancellationToken);

            var atNine = confirmed.Where(appointment => appointment.Period.Overlaps(nineToTen)).ToArray();

            Assert.Equal(2, atNine.Length);
            Assert.Equal(2, atNine.Select(appointment => appointment.ServiceBayId).Distinct().Count());

            if (booking.StatusCode == HttpStatusCode.Created)
            {
                var stayed = confirmed.Single(appointment => appointment.Id == rescheduledId);
                Assert.Equal(new DateTimeOffset(2030, 1, 7, 11, 0, 0, TimeSpan.Zero), stayed.StartAt);
            }

            for (var i = 0; i < confirmed.Count; i++)
            {
                for (var j = i + 1; j < confirmed.Count; j++)
                {
                    var a = confirmed[i];
                    var b = confirmed[j];
                    var overlaps = a.Period.Overlaps(b.Period);

                    Assert.False(overlaps && a.ServiceBayId == b.ServiceBayId, "Overlapping appointments must not share a bay.");
                    Assert.False(overlaps && a.TechnicianId == b.TechnicianId, "Overlapping appointments must not share a technician.");
                    Assert.False(overlaps && a.VehicleId == b.VehicleId, "Overlapping appointments must not share a vehicle.");
                }
            }
        }
    }

    private async Task<Guid[]> CreateVehiclesAsync(int count, CancellationToken cancellationToken)
    {
        var vehicleIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();

        await using var db = _fixture.CreateDbContext();
        foreach (var vehicleId in vehicleIds)
        {
            var vin = vehicleId.ToString("N")[..17].ToUpperInvariant();
            db.Vehicles.Add(new Vehicle(vehicleId, SeedData.Customers.JaneSmith, vin, "Volkswagen", "Golf", 2021));
        }

        await db.SaveChangesAsync(cancellationToken);

        return vehicleIds;
    }

    private async Task<Guid> BookAsync(Guid vehicleId, string startAt)
    {
        using var response = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.JaneSmith,
            vehicleId,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            startAt));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }
}
