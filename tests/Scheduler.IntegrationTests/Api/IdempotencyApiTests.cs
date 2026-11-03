using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using static Scheduler.IntegrationTests.Api.ApiTestHelpers;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class IdempotencyApiTests : IAsyncLifetime
{
    private const string KeyHeader = "Idempotency-Key";

    private readonly PostgresFixture _fixture;
    private readonly SchedulerApiFactory _factory;
    private readonly HttpClient _client;

    public IdempotencyApiTests(PostgresFixture fixture)
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
    public async Task I1_SameRequestTwiceWithSameKey_ReturnsTheSameAppointment()
    {
        var key = Guid.NewGuid().ToString();

        using var first = await PostWithKeyAsync(key, MondayAt0900Utc);
        using var second = await PostWithKeyAsync(key, MondayAt0900Utc);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(
            (await first.ReadJsonAsync()).GetProperty("id").GetGuid(),
            (await second.ReadJsonAsync()).GetProperty("id").GetGuid());
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(1, await CountWithKeyAsync(key));
    }

    [Fact]
    public async Task I2_RetryAfterTheVehicleIsBookedByTheOriginal_ReturnsTheOriginal()
    {
        var key = Guid.NewGuid().ToString();

        using var first = await PostWithKeyAsync(key, MondayAt0900Utc);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var retry = await PostWithKeyAsync(key, MondayAt0900Utc);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(
            (await first.ReadJsonAsync()).GetProperty("id").GetGuid(),
            (await retry.ReadJsonAsync()).GetProperty("id").GetGuid());
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
    }

    [Fact]
    public async Task I4_KeyReusedWithDifferentStart_IsRejectedAndCreatesNothing()
    {
        var key = Guid.NewGuid().ToString();

        using var first = await PostWithKeyAsync(key, MondayAt0900Utc);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var reused = await PostWithKeyAsync(key, "2030-01-07T10:00:00Z");

        await AssertProblemAsync(reused, HttpStatusCode.UnprocessableEntity, "idempotency-key-reused");

        await using var db = _fixture.CreateDbContext();
        var tenOClock = new DateTimeOffset(2030, 1, 7, 10, 0, 0, TimeSpan.Zero);
        Assert.False(await db.Appointments.AnyAsync(
            appointment => appointment.StartAt == tenOClock, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task I6_KeyLongerThanOneHundredCharacters_IsRejected()
    {
        using var response = await PostWithKeyAsync(new string('k', 101), MondayAt0900Utc);

        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation");

        Assert.True(problem.GetProperty("errors").TryGetProperty(KeyHeader, out _));
    }

    [Fact]
    public async Task I5_FiveParallelRequestsWithSameKey_CreateOneAppointment()
    {
        var key = Guid.NewGuid().ToString();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            await gate.Task;
            return await PostWithKeyAsync(key, MondayAt0900Utc);
        }).ToArray();

        gate.SetResult();
        var responses = await Task.WhenAll(tasks);

        try
        {
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }

            var ids = await Task.WhenAll(responses.Select(async response =>
                (await response.ReadJsonAsync()).GetProperty("id").GetGuid()));

            Assert.Single(ids.Distinct());
            Assert.Equal(1, await CountWithKeyAsync(key));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private async Task<HttpResponseMessage> PostWithKeyAsync(string key, string startAt)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/appointments")
        {
            Content = JsonContent.Create(JaneGolfOilChange(startAt)),
        };
        request.Headers.Add(KeyHeader, key);

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<int> CountWithKeyAsync(string key)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.Appointments.CountAsync(
            appointment => appointment.IdempotencyKey == key, TestContext.Current.CancellationToken);
    }
}
