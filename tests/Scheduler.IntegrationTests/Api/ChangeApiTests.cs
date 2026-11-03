using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Scheduler.Domain.Appointments;
using Scheduler.Domain.Scheduling;
using Scheduler.Infrastructure.Seeding;
using static Scheduler.IntegrationTests.Api.ApiTestHelpers;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class ChangeApiTests : IAsyncLifetime
{
    private const string MondayAt0930Utc = "2030-01-07T09:30:00Z";
    private const string MondayAt1100Utc = "2030-01-07T11:00:00Z";

    private readonly PostgresFixture _fixture;
    private readonly SchedulerApiFactory _factory;
    private readonly HttpClient _client;

    public ChangeApiTests(PostgresFixture fixture)
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
    public async Task V1_EmptyDay_ListsEveryQuarterHourFromOpeningToLastStart()
    {
        using var response = await GetAvailabilityAsync("2030-01-07");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.ReadJsonAsync();
        var slots = body.GetProperty("slots").EnumerateArray().ToArray();

        Assert.Equal("Europe/London", body.GetProperty("timeZone").GetString());
        Assert.Equal("2030-01-07", body.GetProperty("date").GetString());
        Assert.Equal(SeedData.Dealerships.London, body.GetProperty("dealershipId").GetGuid());
        Assert.Equal(SeedData.ServiceTypes.OilChange, body.GetProperty("serviceTypeId").GetGuid());
        Assert.Equal(37, slots.Length);
        Assert.Equal(Utc(7, 8, 0), slots[0].GetProperty("startAt").GetDateTimeOffset());
        Assert.Equal(Utc(7, 9, 0), slots[0].GetProperty("endAt").GetDateTimeOffset());
        Assert.Equal(Utc(7, 17, 0), slots[^1].GetProperty("startAt").GetDateTimeOffset());
        Assert.Equal(Utc(7, 18, 0), slots[^1].GetProperty("endAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task V3_CancelledAppointments_DoNotBlockTheirTime()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);
        var tesla = await BookAsync(SeedData.Vehicles.JaneTesla, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        Assert.DoesNotContain(Utc(7, 9, 0), await ListAvailabilityStartsAsync("2030-01-07"));

        await CancelAsync(golf);
        await CancelAsync(tesla);

        Assert.Contains(Utc(7, 9, 0), await ListAvailabilityStartsAsync("2030-01-07"));
    }

    [Theory]
    [InlineData("2030-01-06")]
    [InlineData("2020-01-06")]
    public async Task V5_ClosedDayOrPastDate_ReturnsEmptyList(string date)
    {
        using var response = await GetAvailabilityAsync(date);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.ReadJsonAsync()).GetProperty("slots").EnumerateArray());
    }

    [Fact]
    public async Task V7_ServiceNotOfferedAtDealership_ReturnsUnprocessableEntity()
    {
        using var response = await _client.GetAsync(
            $"/dealerships/{SeedData.Dealerships.Saigon}/availability?serviceTypeId={SeedData.ServiceTypes.WheelAlignment}&date=2030-01-07",
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "service-not-offered");
    }

    [Fact]
    public async Task V8_UnknownDealership_ReturnsNotFound()
    {
        using var response = await _client.GetAsync(
            $"/dealerships/{Guid.NewGuid()}/availability?serviceTypeId={SeedData.ServiceTypes.OilChange}&date=2030-01-07",
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    [Theory]
    [InlineData("serviceTypeId=5e000000-0000-4000-8000-000000000001", "date")]
    [InlineData("serviceTypeId=5e000000-0000-4000-8000-000000000001&date=07/01/2030", "date")]
    [InlineData("date=2030-01-07", "serviceTypeId")]
    public async Task V8_MissingOrMalformedQuery_ReturnsValidationProblem(string query, string field)
    {
        using var response = await _client.GetAsync(
            $"/dealerships/{SeedData.Dealerships.London}/availability?{query}",
            TestContext.Current.CancellationToken);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");

        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task A1_Alternatives_TreatTheAppointmentsOwnSlotAsFree()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);
        await BookAsync(SeedData.Vehicles.JaneTesla, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        using var response = await _client.GetAsync(
            $"/appointments/{golf}/alternatives",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Utc(7, 9, 30), StartsOf(await response.ReadJsonAsync()));
    }

    [Fact]
    public async Task A3_AlternativesForAnotherDate_ListThatDaysSlots()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        using var response = await _client.GetAsync(
            $"/appointments/{golf}/alternatives?date=2030-01-08",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.ReadJsonAsync();
        var starts = StartsOf(body);

        Assert.Equal("2030-01-08", body.GetProperty("date").GetString());
        Assert.Equal(37, starts.Count);
        Assert.Equal(new DateTimeOffset(2030, 1, 8, 8, 0, 0, TimeSpan.Zero), starts[0]);
    }

    [Fact]
    public async Task A4_AlternativesForCancelledAppointment_ReturnsAppointmentCancelled()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);
        await CancelAsync(golf);

        using var response = await _client.GetAsync(
            $"/appointments/{golf}/alternatives",
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "appointment-cancelled");
    }

    [Fact]
    public async Task A4_AlternativesWithinCutoff_ReturnsChangeWindowClosed()
    {
        var id = await InsertAppointmentAsync(DateTimeOffset.UtcNow.AddHours(1));

        using var response = await _client.GetAsync(
            $"/appointments/{id}/alternatives",
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "change-window-closed");
    }

    [Fact]
    public async Task A4_AlternativesForUnknownAppointment_ReturnsNotFound()
    {
        using var response = await _client.GetAsync(
            $"/appointments/{Guid.NewGuid()}/alternatives",
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task R1_RescheduleToFreeTime_KeepsIdAndChangesTimes()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        using var response = await RescheduleAsync(golf, MondayAt1100Utc);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.ReadJsonAsync();

        Assert.Equal(golf, body.GetProperty("id").GetGuid());
        Assert.Equal(Utc(7, 11, 0), body.GetProperty("startAt").GetDateTimeOffset());
        Assert.Equal(Utc(7, 12, 0), body.GetProperty("endAt").GetDateTimeOffset());
        Assert.Equal("Confirmed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task R2_RescheduleOverlappingOldTime_Succeeds()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);
        await BookAsync(SeedData.Vehicles.JaneTesla, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        using var response = await RescheduleAsync(golf, MondayAt0930Utc);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Utc(7, 9, 30), (await response.ReadJsonAsync()).GetProperty("startAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task R3_NoCapacityAtNewTime_ReturnsConflictAndKeepsOldTime()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);
        await BookAsync(SeedData.Vehicles.JaneTesla, SeedData.Customers.JaneSmith, MondayAt1100Utc);
        await BookAsync(SeedData.Vehicles.AnVinfast, SeedData.Customers.NguyenVanAn, MondayAt1100Utc);

        using var response = await RescheduleAsync(golf, MondayAt1100Utc);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "no-capacity");

        using var read = await _client.GetAsync($"/appointments/{golf}", TestContext.Current.CancellationToken);
        var body = await read.ReadJsonAsync();

        Assert.Equal(Utc(7, 9, 0), body.GetProperty("startAt").GetDateTimeOffset());
        Assert.Equal("Confirmed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task R7_RescheduleCancelledAppointment_ReturnsAppointmentCancelled()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);
        await CancelAsync(golf);

        using var response = await RescheduleAsync(golf, MondayAt1100Utc);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "appointment-cancelled");
    }

    [Fact]
    public async Task R9_AfterMove_AnotherVehicleCanBookTheOldTime()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);
        await BookAsync(SeedData.Vehicles.JaneTesla, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        using var moved = await RescheduleAsync(golf, MondayAt1100Utc);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        using var booked = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.NguyenVanAn,
            SeedData.Vehicles.AnVinfast,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            MondayAt0900Utc));

        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
    }

    [Fact]
    public async Task C1_Cancel_ReturnsCancelledWithCancelledAt()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        using var response = await _client.PostAsync($"/appointments/{golf}/cancel", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cancelled = await response.ReadJsonAsync();

        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, cancelled.GetProperty("cancelledAt").ValueKind);

        using var read = await _client.GetAsync($"/appointments/{golf}", TestContext.Current.CancellationToken);
        var readBack = await read.ReadJsonAsync();

        Assert.Equal("Cancelled", readBack.GetProperty("status").GetString());
        Assert.Equal(
            cancelled.GetProperty("cancelledAt").GetDateTimeOffset(),
            readBack.GetProperty("cancelledAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task C2_AfterCancel_TimeIsListedAndCanBeBooked()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);
        await BookAsync(SeedData.Vehicles.JaneTesla, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        Assert.DoesNotContain(Utc(7, 9, 0), await ListAvailabilityStartsAsync("2030-01-07"));

        await CancelAsync(golf);

        Assert.Contains(Utc(7, 9, 0), await ListAvailabilityStartsAsync("2030-01-07"));

        using var booked = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.NguyenVanAn,
            SeedData.Vehicles.AnVinfast,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            MondayAt0900Utc));

        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
    }

    [Fact]
    public async Task C3_CancelTwice_ReturnsSameCancelledAt()
    {
        var golf = await BookAsync(SeedData.Vehicles.JaneGolf, SeedData.Customers.JaneSmith, MondayAt0900Utc);

        using var first = await _client.PostAsync($"/appointments/{golf}/cancel", null, TestContext.Current.CancellationToken);
        using var second = await _client.PostAsync($"/appointments/{golf}/cancel", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            (await first.ReadJsonAsync()).GetProperty("cancelledAt").GetDateTimeOffset(),
            (await second.ReadJsonAsync()).GetProperty("cancelledAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(60)]
    [InlineData(-10)]
    public async Task C4_CancelWithinCutoffOrStarted_ReturnsChangeWindowClosed(int minutesFromNow)
    {
        var id = await InsertAppointmentAsync(DateTimeOffset.UtcNow.AddMinutes(minutesFromNow));

        using var response = await _client.PostAsync($"/appointments/{id}/cancel", null, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "change-window-closed");
    }

    [Fact]
    public async Task C5_CancelUnknownAppointment_ReturnsNotFound()
    {
        using var response = await _client.PostAsync(
            $"/appointments/{Guid.NewGuid()}/cancel",
            null,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task X1_CutoffFromConfiguration_AllowsCancellingBeyondOneHour()
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Scheduling:ChangeCutoff"] = "01:00:00",
                })));
        using var client = factory.CreateClient();

        var id = await InsertAppointmentAsync(DateTimeOffset.UtcNow.AddMinutes(75));

        using var response = await client.PostAsync($"/appointments/{id}/cancel", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", (await response.ReadJsonAsync()).GetProperty("status").GetString());
    }

    private static DateTimeOffset Utc(int day, int hour, int minute) =>
        new(2030, 1, day, hour, minute, 0, TimeSpan.Zero);

    private static List<DateTimeOffset> StartsOf(JsonElement body) =>
        body.GetProperty("slots").EnumerateArray()
            .Select(slot => slot.GetProperty("startAt").GetDateTimeOffset())
            .ToList();

    private Task<HttpResponseMessage> GetAvailabilityAsync(string date) =>
        _client.GetAsync(
            $"/dealerships/{SeedData.Dealerships.London}/availability?serviceTypeId={SeedData.ServiceTypes.OilChange}&date={date}",
            TestContext.Current.CancellationToken);

    private async Task<List<DateTimeOffset>> ListAvailabilityStartsAsync(string date)
    {
        using var response = await GetAvailabilityAsync(date);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return StartsOf(await response.ReadJsonAsync());
    }

    private async Task<Guid> BookAsync(Guid vehicleId, Guid customerId, string startAt)
    {
        using var response = await _client.PostBookingAsync(BookingRequest(
            customerId,
            vehicleId,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            startAt));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private async Task CancelAsync(Guid appointmentId)
    {
        using var response = await _client.PostAsync(
            $"/appointments/{appointmentId}/cancel",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> RescheduleAsync(Guid appointmentId, string startAt) =>
        _client.PostAsJsonAsync(
            $"/appointments/{appointmentId}/reschedule",
            new { startAt },
            TestContext.Current.CancellationToken);

    private async Task<Guid> InsertAppointmentAsync(DateTimeOffset start)
    {
        var id = Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();
        db.Appointments.Add(Appointment.Confirm(
            id,
            SeedData.Dealerships.London,
            SeedData.Customers.JaneSmith,
            SeedData.Vehicles.JaneGolf,
            SeedData.ServiceTypes.OilChange,
            SeedData.Bays.LondonBay1,
            SeedData.Technicians.Alice,
            TimeRange.FromDuration(start.ToUniversalTime(), TimeSpan.FromHours(1)),
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return id;
    }
}
