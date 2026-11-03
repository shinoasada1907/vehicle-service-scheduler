using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Scheduler.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public sealed class HealthTests : IAsyncLifetime
{
    private readonly SchedulerApiFactory _factory;
    private readonly HttpClient _client;

    public HealthTests(PostgresFixture fixture)
    {
        _factory = new SchedulerApiFactory(fixture);
        _client = _factory.CreateClient();
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task O1_Live_ReturnsOk()
    {
        using var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task O2_Ready_WithDatabaseUp_ReturnsOk()
    {
        using var response = await _client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task O3_UnreachableDatabase_ReadyIsServiceUnavailableAndLiveStaysOk()
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Scheduler"] =
                        "Host=localhost;Port=1;Database=scheduler;Username=scheduler;Password=unused;Timeout=2",
                })));
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        using var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }
}
