using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace WmsResourcePlanner.Infrastructure.Data;

/// <summary>
/// Backup/restore for the SQLite database file (SPEC.md section 95).
/// Uses SQLite's online backup API so it is safe to call while the
/// application's own connection pool may still have open connections.
/// </summary>
public class BackupService
{
    private readonly string _dbPath;
    private readonly string _backupDir;

    public BackupService(IConfiguration configuration)
    {
        var connString = configuration.GetConnectionString("Default") ?? "Data Source=App_Data/wmsresourceplanner.db";
        var builder = new SqliteConnectionStringBuilder(connString);
        _dbPath = Path.GetFullPath(builder.DataSource);
        _backupDir = Path.Combine(Path.GetDirectoryName(_dbPath) ?? ".", "Backups");
        Directory.CreateDirectory(_backupDir);
    }

    public List<(string FileName, DateTime CreatedUtc, long SizeBytes)> ListBackups()
    {
        return Directory.GetFiles(_backupDir, "*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => (f.Name, f.CreationTimeUtc, f.Length))
            .ToList();
    }

    public async Task<string> BackupAsync(CancellationToken ct = default)
    {
        var fileName = $"backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db";
        var destPath = Path.Combine(_backupDir, fileName);

        using var source = new SqliteConnection($"Data Source={_dbPath}");
        using var destination = new SqliteConnection($"Data Source={destPath}");
        await source.OpenAsync(ct);
        await destination.OpenAsync(ct);
        source.BackupDatabase(destination);

        return fileName;
    }

    public async Task RestoreAsync(string fileName, CancellationToken ct = default)
    {
        var sourcePath = Path.Combine(_backupDir, fileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Backup file not found.", sourcePath);
        }

        using var source = new SqliteConnection($"Data Source={sourcePath}");
        using var destination = new SqliteConnection($"Data Source={_dbPath}");
        await source.OpenAsync(ct);
        await destination.OpenAsync(ct);
        source.BackupDatabase(destination);
    }
}
