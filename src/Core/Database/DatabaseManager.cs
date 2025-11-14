using Dapper;
using Microsoft.Data.Sqlite;

namespace ARK.Core.Database;

/// <summary>
/// Manages SQLite database connections and initialization
/// </summary>
public class DatabaseManager : IAsyncDisposable
{
    private readonly string _databasePath;
    private SqliteConnection? _connection;

    public DatabaseManager(string? databasePath = null)
    {
        var dbDirectory = databasePath ?? Path.Combine(AppContext.BaseDirectory, "db");
        Directory.CreateDirectory(dbDirectory);
        _databasePath = Path.Combine(dbDirectory, "ark-retro-forge.sqlite");
    }

    /// <summary>
    /// Initialize the database and ensure schema is up to date
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWriteCreate");
        await _connection.OpenAsync(cancellationToken);

        // Enable WAL mode for better concurrency
        if (_connection != null)
        {
            await _connection.ExecuteAsync("PRAGMA journal_mode=WAL;");
            await _connection.ExecuteAsync("PRAGMA synchronous=NORMAL;");
            await _connection.ExecuteAsync("PRAGMA temp_store=MEMORY;");
            await _connection.ExecuteAsync("PRAGMA mmap_size=30000000000;");
        }

        await CreateSchemaAsync();
    }

    /// <summary>
    /// Create initial schema
    /// </summary>
    private async Task CreateSchemaAsync()
    {
        // Schema version table
        await _connection!.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                description TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
        ");

        // ROM cache table
        await _connection!.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS rom_cache (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL UNIQUE,
                file_size INTEGER NOT NULL,
                crc32 TEXT,
                md5 TEXT,
                sha1 TEXT,
                last_verified TEXT NOT NULL,
                system_id TEXT,
                title TEXT,
                region TEXT,
                rom_id TEXT
            );
        ");

        // Create indices
        await _connection!.ExecuteAsync(@"
            CREATE INDEX IF NOT EXISTS idx_rom_cache_path ON rom_cache(file_path);
            CREATE INDEX IF NOT EXISTS idx_rom_cache_crc32 ON rom_cache(crc32);
            CREATE INDEX IF NOT EXISTS idx_rom_cache_md5 ON rom_cache(md5);
            CREATE INDEX IF NOT EXISTS idx_rom_cache_sha1 ON rom_cache(sha1);
        ");

        // Record schema version
        var currentVersion = await GetCurrentSchemaVersionAsync();
        if (currentVersion == 0)
        {
            await _connection!.ExecuteAsync(@"
                INSERT OR IGNORE INTO schema_version (version, description, applied_at)
                VALUES (1, 'Initial schema', @AppliedAt);
            ", new { AppliedAt = DateTime.UtcNow.ToString("O") });
        }
    }

    /// <summary>
    /// Get current schema version
    /// </summary>
    public async Task<int> GetCurrentSchemaVersionAsync()
    {
        try
        {
            return await _connection!.QuerySingleOrDefaultAsync<int>(
                "SELECT COALESCE(MAX(version), 0) FROM schema_version");
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Get database connection
    /// </summary>
    public SqliteConnection GetConnection()
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
        }
        return _connection;
    }

    /// <summary>
    /// Vacuum database to reclaim space
    /// </summary>
    public async Task VacuumAsync()
    {
        await _connection!.ExecuteAsync("VACUUM;");
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}
