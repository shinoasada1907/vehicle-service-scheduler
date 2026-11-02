using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Scheduler.Infrastructure.Seeding;

namespace Scheduler.IntegrationTests.Api;

internal static class ApiTestHelpers
{
    public const string MondayAt0900Utc = "2030-01-07T09:00:00Z";

    public static object BookingRequest(
        Guid customerId,
        Guid vehicleId,
        Guid dealershipId,
        Guid serviceTypeId,
        string startAt) =>
        new { customerId, vehicleId, dealershipId, serviceTypeId, startAt };

    public static object JaneGolfOilChange(string startAt) =>
        BookingRequest(
            SeedData.Customers.JaneSmith,
            SeedData.Vehicles.JaneGolf,
            SeedData.Dealerships.London,
            SeedData.ServiceTypes.OilChange,
            startAt);

    public static Task<HttpResponseMessage> PostBookingAsync(this HttpClient client, object request) =>
        client.PostAsJsonAsync("/appointments", request, TestContext.Current.CancellationToken);

    public static Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    public static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string slug)
    {
        Assert.Equal(status, response.StatusCode);

        var problem = await response.ReadJsonAsync();

        Assert.Equal($"urn:vehicle-service-scheduler:problem:{slug}", problem.GetProperty("type").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        AssertNotBlank(problem, "title");
        AssertNotBlank(problem, "detail");
        AssertNotBlank(problem, "traceId");

        return problem;
    }

    private static void AssertNotBlank(JsonElement problem, string property) =>
        Assert.False(
            string.IsNullOrWhiteSpace(problem.GetProperty(property).GetString()),
            $"Problem Details property '{property}' must not be blank.");
}
