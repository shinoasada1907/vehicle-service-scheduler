using System.Net;
using System.Net.Http.Json;
using System.Text;
using Scheduler.Infrastructure.Seeding;
using static Scheduler.IntegrationTests.Api.ApiTestHelpers;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class AppointmentApiTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly SchedulerApiFactory _factory;
    private readonly HttpClient _client;

    public AppointmentApiTests(PostgresFixture fixture)
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
    public async Task S1_ValidBooking_ReturnsCreatedWithBayAndTechnician()
    {
        using var response = await _client.PostBookingAsync(JaneGolfOilChange(MondayAt0900Utc));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var appointment = await response.ReadJsonAsync();
        var id = appointment.GetProperty("id").GetGuid();

        Assert.Equal($"/appointments/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Confirmed", appointment.GetProperty("status").GetString());
        Assert.Equal("London Bay 1", appointment.GetProperty("serviceBay").GetProperty("name").GetString());
        Assert.Equal("Alice Walker", appointment.GetProperty("technician").GetProperty("name").GetString());
        Assert.Equal(
            new DateTimeOffset(2030, 1, 7, 9, 0, 0, TimeSpan.Zero),
            appointment.GetProperty("startAt").GetDateTimeOffset());
        Assert.Equal(
            new DateTimeOffset(2030, 1, 7, 10, 0, 0, TimeSpan.Zero),
            appointment.GetProperty("endAt").GetDateTimeOffset());
        Assert.Equal("Europe/London", appointment.GetProperty("dealership").GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task S3_StartWithNonUtcOffset_IsReturnedInUtc()
    {
        using var response = await _client.PostBookingAsync(JaneGolfOilChange("2030-01-07T10:00:00+01:00"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var startAt = (await response.ReadJsonAsync()).GetProperty("startAt").GetDateTimeOffset();

        Assert.Equal(new DateTimeOffset(2030, 1, 7, 9, 0, 0, TimeSpan.Zero), startAt);
        Assert.Equal(TimeSpan.Zero, startAt.Offset);
    }

    [Fact]
    public async Task S4_BookedAppointment_CanBeReadBackById()
    {
        using var created = await _client.PostBookingAsync(JaneGolfOilChange(MondayAt0900Utc));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var booked = await created.ReadJsonAsync();

        using var response = await _client.GetAsync(created.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var appointment = await response.ReadJsonAsync();

        Assert.Equal(booked.GetProperty("id").GetGuid(), appointment.GetProperty("id").GetGuid());
        Assert.Equal("Alice Walker", appointment.GetProperty("technician").GetProperty("name").GetString());
        Assert.Equal("Oil change", appointment.GetProperty("serviceType").GetProperty("name").GetString());
    }

    [Fact]
    public async Task S11_EveryCompatibleBayBusy_ReturnsNoCapacityForBays()
    {
        using var golf = await _client.PostBookingAsync(JaneGolfOilChange(MondayAt0900Utc));
        using var tesla = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.JaneSmith,
            SeedData.Vehicles.JaneTesla,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            MondayAt0900Utc));

        Assert.Equal(HttpStatusCode.Created, golf.StatusCode);
        Assert.Equal(HttpStatusCode.Created, tesla.StatusCode);

        using var response = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.NguyenVanAn,
            SeedData.Vehicles.AnVinfast,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            MondayAt0900Utc));

        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, "no-capacity");

        Assert.Equal("bays", problem.GetProperty("shortage").GetString());
    }

    [Fact]
    public async Task S14_ServiceTheDealershipLacksABayFor_ReturnsServiceNotOffered()
    {
        using var response = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.NguyenVanAn,
            SeedData.Vehicles.AnVinfast,
            SeedData.Dealerships.Saigon,
            SeedData.ServiceTypes.WheelAlignment,
            "2030-01-07T02:00:00Z"));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "service-not-offered");
    }

    [Fact]
    public async Task S16_VehicleOfAnotherCustomer_ReturnsVehicleNotOwned()
    {
        using var response = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.JaneSmith,
            SeedData.Vehicles.AnVinfast,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            MondayAt0900Utc));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "vehicle-not-owned");
    }

    [Fact]
    public async Task S17_StartInThePast_ReturnsInvalidStartTime()
    {
        using var response = await _client.PostBookingAsync(JaneGolfOilChange("2020-01-06T09:00:00Z"));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "invalid-start-time");
    }

    [Fact]
    public async Task S19_SundayWhenTheDealershipIsClosed_ReturnsOutsideOpeningHours()
    {
        using var response = await _client.PostBookingAsync(JaneGolfOilChange("2030-01-06T10:00:00Z"));

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "outside-opening-hours");
    }

    [Fact]
    public async Task S22_UnknownDealership_ReturnsNotFound()
    {
        using var response = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.JaneSmith,
            SeedData.Vehicles.JaneGolf,
            Guid.NewGuid(),
            SeedData.ServiceTypes.OilChange,
            MondayAt0900Utc));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task S23_EmptyBody_ReturnsValidationErrorForEveryField()
    {
        using var response = await _client.PostBookingAsync(new { });

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");

        var fields = problem.GetProperty("errors").EnumerateObject().Select(error => error.Name);

        Assert.Equal(
            ["customerId", "dealershipId", "serviceTypeId", "startAt", "vehicleId"],
            fields.Order());
    }

    [Fact]
    public async Task S24_StartWithoutOffset_ReturnsValidationErrorForStartAt()
    {
        using var response = await _client.PostBookingAsync(JaneGolfOilChange("2030-01-07T09:00:00"));

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");

        Assert.True(problem.GetProperty("errors").TryGetProperty("startAt", out _));
    }

    [Fact]
    public async Task E5_FieldWithWrongType_ReturnsValidationProblem()
    {
        using var content = new StringContent("""{"customerId":"abc"}""", Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("/appointments", content, TestContext.Current.CancellationToken);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");

        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task E6_AppointmentIdThatIsNotAGuid_ReturnsNotFound()
    {
        using var response = await _client.GetAsync("/appointments/abc", TestContext.Current.CancellationToken);

        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");

        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task S25_UnknownAppointment_ReturnsNotFound()
    {
        using var response = await _client.GetAsync($"/appointments/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not-found");
    }

    [Fact]
    public async Task S30_OverlappingBookingForTheSameVehicle_ReturnsVehicleAlreadyBooked()
    {
        using var first = await _client.PostBookingAsync(JaneGolfOilChange(MondayAt0900Utc));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var response = await _client.PostBookingAsync(BookingRequest(
            SeedData.Customers.JaneSmith,
            SeedData.Vehicles.JaneGolf,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.BrakeInspection,
            "2030-01-07T09:30:00Z"));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "vehicle-already-booked");
    }
}
