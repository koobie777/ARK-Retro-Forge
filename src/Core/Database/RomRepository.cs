using Dapper;
using Microsoft.Data.Sqlite;

namespace ARK.Core.Database;

/// <summary>
/// Provides helpers for storing and retrieving ROM metadata in the local cache.
/// </summary>
public class RomRepository
{
    private readonly SqliteConnection _connection;

    public RomRepository(SqliteConnection connection)
    {
        _connection = connection;
    }

    public Task UpsertRomAsync(RomRecord record)
    {
        const string sql = @"
INSERT INTO rom_cache (file_path, file_size, last_verified, system_id, title, region, rom_id)
VALUES (@FilePath, @FileSize, @LastSeen, @SystemId, @Title, @Region, @RomId)
ON CONFLICT(file_path) DO UPDATE SET
    file_size = excluded.file_size,
    last_verified = CASE WHEN excluded.last_verified IS NOT NULL THEN excluded.last_verified ELSE rom_cache.last_verified END,
    system_id = CASE WHEN excluded.system_id IS NOT NULL THEN excluded.system_id ELSE rom_cache.system_id END,
    title = CASE WHEN excluded.title IS NOT NULL THEN excluded.title ELSE rom_cache.title END,
    region = CASE WHEN excluded.region IS NOT NULL THEN excluded.region ELSE rom_cache.region END,
    rom_id = CASE WHEN excluded.rom_id IS NOT NULL THEN excluded.rom_id ELSE rom_cache.rom_id END;
";

        return _connection.ExecuteAsync(sql, new
        {
            record.FilePath,
            record.FileSize,
            LastSeen = record.LastSeen.ToString("O"),
            record.SystemId,
            record.Title,
            record.Region,
            record.RomId
        });
    }

    public Task UpdateHashesAsync(RomHashUpdate update)
    {
        const string sql = @"
UPDATE rom_cache
SET crc32 = COALESCE(@Crc32, crc32),
    md5 = COALESCE(@Md5, md5),
    sha1 = COALESCE(@Sha1, sha1),
    file_size = @FileSize,
    last_verified = @LastVerified
WHERE file_path = @FilePath;
";

        return _connection.ExecuteAsync(sql, new
        {
            update.FilePath,
            update.Crc32,
            update.Md5,
            update.Sha1,
            FileSize = update.FileSize,
            LastVerified = update.LastVerified.ToString("O")
        });
    }

    public Task<RomCacheEntry?> GetByPathAsync(string filePath)
    {
        const string sql = "SELECT * FROM rom_cache WHERE file_path = @FilePath LIMIT 1;";
        return _connection.QuerySingleOrDefaultAsync<RomCacheEntry>(sql, new { FilePath = filePath });
    }
}

public record RomRecord(
    string FilePath,
    long FileSize,
    DateTime LastSeen,
    string? SystemId,
    string? Title,
    string? Region,
    string? RomId);

public record RomHashUpdate(
    string FilePath,
    string? Crc32,
    string? Md5,
    string? Sha1,
    long FileSize,
    DateTime LastVerified);

public record RomCacheEntry
{
    public long Id { get; init; }
    public string File_Path { get; init; } = string.Empty;
    public long File_Size { get; init; }
    public string? Crc32 { get; init; }
    public string? Md5 { get; init; }
    public string? Sha1 { get; init; }
    public string? Last_Verified { get; init; }
    public string? System_Id { get; init; }
    public string? Title { get; init; }
    public string? Region { get; init; }
    public string? Rom_Id { get; init; }
}
