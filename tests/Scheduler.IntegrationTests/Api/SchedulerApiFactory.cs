using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Scheduler.IntegrationTests.Api;

public sealed class SchedulerApiFactory(PostgresFixture database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Scheduler"] = database.ConnectionString,
                ["Database:MigrateOnStartup"] = "false",
            }));
    }
}
