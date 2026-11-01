using Microsoft.EntityFrameworkCore;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Infrastructure.Seeding;
using Testcontainers.PostgreSql;

namespace Scheduler.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await _container.StartAsync(cancellationToken);

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync(cancellationToken);
        await DatabaseSeeder.SeedAsync(db, cancellationToken);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public SchedulerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new SchedulerDbContext(options);
    }

    public async Task ResetAppointmentsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE appointments", cancellationToken);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
