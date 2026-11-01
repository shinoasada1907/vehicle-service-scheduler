using System.Net;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class ApiDocumentationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly SchedulerApiFactory _factory;
    private readonly HttpClient _client;

    public ApiDocumentationTests(PostgresFixture fixture)
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
    public async Task E3_OpenApiDocument_DescribesTheAppointmentsEndpoints()
    {
        using var response = await _client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/appointments", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task E4_ScalarReference_IsServed()
    {
        using var response = await _client.GetAsync("/scalar", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"GET /scalar returned {(int)response.StatusCode}.");
    }
}
