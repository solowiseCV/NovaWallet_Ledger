using Testcontainers.PostgreSql;
using Xunit;

namespace NovaWallet.Infrastructure.Tests.Fixtures;

/// <summary>
/// Spins up a real PostgreSQL container so these tests exercise the actual
/// "SELECT ... FOR UPDATE" row-locking behaviour EfWalletRepository relies on.
/// An in-memory or SQLite provider would NOT catch concurrency bugs here, since
/// neither implements real row-level locking the same way — see AI_USAGE.md.
/// </summary>
public class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("novawallet_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("Postgres collection")]
public class PostgresCollection : ICollectionFixture<PostgresContainerFixture> { }
