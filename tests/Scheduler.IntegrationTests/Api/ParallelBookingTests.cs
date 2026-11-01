using System.Net;
using Microsoft.EntityFrameworkCore;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Customers;
using Scheduler.Infrastructure.Seeding;
using static Scheduler.IntegrationTests.Api.ApiTestHelpers;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class ParallelBookingTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly SchedulerApiFactory _factory;
    private readonly HttpClient _client;

    public ParallelBookingTests(PostgresFixture fixture)
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
    public async Task S26_TwelveParallelBookingsForTwoBays_ConfirmExactlyTwo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var vehicleIds = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();

        await using (var db = _fixture.CreateDbContext())
        {
            foreach (var vehicleId in vehicleIds)
            {
                var vin = vehicleId.ToString("N")[..17].ToUpperInvariant();
                db.Vehicles.Add(new Vehicle(vehicleId, SeedData.Customers.JaneSmith, vin, "Volkswagen", "Golf", 2021));
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var bookingTasks = vehicleIds.Select(async vehicleId =>
        {
            await gate.Task;
            return await _client.PostBookingAsync(BookingRequest(
                SeedData.Customers.JaneSmith,
                vehicleId,
                SeedData.Dealerships.London,
                SeedData.ServiceTypes.OilChange,
                MondayAt0900Utc));
        }).ToArray();

        gate.SetResult();
        var responses = await Task.WhenAll(bookingTasks);

        try
        {
            var created = responses.Where(response => response.StatusCode == HttpStatusCode.Created).ToArray();
            var conflicted = responses.Where(response => response.StatusCode == HttpStatusCode.Conflict).ToArray();

            Assert.Equal(2, created.Length);
            Assert.Equal(10, conflicted.Length);

            foreach (var response in conflicted)
            {
                await AssertProblemAsync(response, HttpStatusCode.Conflict, "no-capacity");
            }

            var bayIds = await Task.WhenAll(created.Select(async response =>
                (await response.ReadJsonAsync()).GetProperty("serviceBay").GetProperty("id").GetGuid()));

            Assert.Equal(2, bayIds.Distinct().Count());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var verificationDb = _fixture.CreateDbContext();
        var confirmed = await verificationDb.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.Status == AppointmentStatus.Confirmed)
            .ToListAsync(cancellationToken);

        Assert.Equal(2, confirmed.Count);

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
