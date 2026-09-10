using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WmsResourcePlanner.Infrastructure.Data;

namespace WmsResourcePlanner.Tests;

/// <summary>
/// Provides an isolated in-memory SQLite AppDbContext per test, with schema
/// created via EnsureCreated (no migrations needed for unit tests).
/// </summary>
public static class TestDbFactory
{
    public static AppDbContext Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new TestAppDbContext(options, connection);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Keeps the underlying SqliteConnection alive for the lifetime of the
    /// context (in-memory SQLite databases are destroyed when the connection
    /// closes) and disposes it when the context is disposed.
    /// </summary>
    private class TestAppDbContext : AppDbContext
    {
        private readonly SqliteConnection _connection;

        public TestAppDbContext(DbContextOptions<AppDbContext> options, SqliteConnection connection) : base(options)
        {
            _connection = connection;
        }

        public override void Dispose()
        {
            base.Dispose();
            _connection.Dispose();
        }
    }
}
