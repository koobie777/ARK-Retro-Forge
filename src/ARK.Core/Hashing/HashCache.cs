using ARK.Core.Instances;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ARK.Core.Hashing;

/// <summary>
/// Per-instance SQLite cache of computed ROM hashes, keyed <b>path + size + mtime</b>.
/// </summary>
/// <remarks>
/// <para>
/// A full hash pass over a reference-sized drive takes hours, so a second run over an unchanged
/// set must do approximately no work. The key is what makes that safe: any write to a file moves
/// its timestamp, so a stale entry can never be served.
/// </para>
/// <para>
/// That same property covers a real hazard for free — a hash taken while a file was still
/// downloading self-invalidates on the next write rather than persisting as a wrong answer.
/// </para>
/// <para>
/// Entries are committed as they are produced, never batched to the end, so cancelling a
/// multi-hour run keeps every hash it had already computed.
/// </para>
/// </remarks>
public sealed class HashCache
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS hashes (
            path        TEXT NOT NULL COLLATE NOCASE,
            size        INTEGER NOT NULL,
            mtime_ticks INTEGER NOT NULL,
            crc32       TEXT NOT NULL,
            sha1        TEXT,
            rom_size    INTEGER NOT NULL,
            format      TEXT NOT NULL,
            normalized  INTEGER NOT NULL,
            computed_utc TEXT NOT NULL,
            PRIMARY KEY (path, size, mtime_ticks)
        );
        """;

    private readonly string _databasePath;
    private SqliteConnection? _connection;

    /// <summary>Creates a cache backed by the instance's <c>db/hashes.db</c>.</summary>
    public HashCache(InstancePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _databasePath = paths.HashCacheDatabase;
    }

    /// <summary>Hashes served from cache during this session.</summary>
    public int Hits { get; private set; }

    /// <summary>Hashes computed and stored during this session.</summary>
    public int Misses { get; private set; }

    /// <summary>
    /// Looks up a previously computed hash. Returns null when absent, or when the file's size or
    /// timestamp has moved since — a changed file is a cache miss, never a stale hit.
    /// </summary>
    public RomHash? TryGet(string path, long size, DateTimeOffset modifiedUtc)
    {
        if (!File.Exists(_databasePath))
        {
            return null;
        }

        var row = Connection().QueryFirstOrDefault<CacheRow>(
            "SELECT crc32 AS Crc32, sha1 AS Sha1, rom_size AS RomSize, format AS Format, normalized AS Normalized " +
            "FROM hashes WHERE path = @path AND size = @size AND mtime_ticks = @ticks;",
            new { path, size, ticks = modifiedUtc.UtcTicks });

        if (row is null)
        {
            return null;
        }

        Hits++;

        // Stored by NAME, not ordinal. An ordinal would be silently reinterpreted whenever the
        // enum changed — removing one member once relabelled 592 cached Game Boy ROMs as NES.
        // An unrecognized name degrades to Unknown; the CRC32, which is what the cache exists for,
        // is unaffected either way.
        var format = Enum.TryParse<RomFormat>(row.Format, out var parsed) ? parsed : RomFormat.Unknown;
        return new RomHash(
            row.Crc32,
            row.Sha1,
            row.RomSize,
            new RomFormatDetection(format, QualifierFor(format), "from cache"),
            row.Normalized != 0);
    }

    /// <summary>
    /// Stores a computed hash. Committed immediately — a cancelled run must not discard work it
    /// already did.
    /// </summary>
    public void Store(string path, long size, DateTimeOffset modifiedUtc, RomHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        Connection().Execute(
            """
            INSERT OR REPLACE INTO hashes
                (path, size, mtime_ticks, crc32, sha1, rom_size, format, normalized, computed_utc)
            VALUES (@path, @size, @ticks, @crc32, @sha1, @romSize, @format, @normalized, @computedUtc);
            """,
            new
            {
                path,
                size,
                ticks = modifiedUtc.UtcTicks,
                crc32 = hash.Crc32,
                sha1 = hash.Sha1,
                romSize = hash.Size,
                format = hash.Format.Format.ToString(),
                normalized = hash.Normalized ? 1 : 0,
                computedUtc = DateTimeOffset.UtcNow.ToString("o"),
            });

        Misses++;
    }

    /// <summary>Number of cached hashes.</summary>
    public int Count() => File.Exists(_databasePath)
        ? Connection().ExecuteScalar<int>("SELECT COUNT(1) FROM hashes;")
        : 0;

    /// <summary>Closes the underlying connection.</summary>
    public void Close()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private SqliteConnection Connection()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        // Pooling off so the file handle is released with the connection, matching the DAT catalog.
        _connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        _connection.Open();
        _connection.Execute(SchemaSql);
        return _connection;
    }

    private static string? QualifierFor(RomFormat format) => format switch
    {
        RomFormat.N64BigEndian => "BigEndian",
        RomFormat.N64ByteSwapped => "ByteSwapped",
        RomFormat.N64LittleEndian => "LittleEndian",
        RomFormat.SnesHeadered or RomFormat.NesINes or RomFormat.NesNes20 => "Headered",
        _ => null,
    };

    private sealed record CacheRow
    {
        public string Crc32 { get; init; } = string.Empty;

        public string? Sha1 { get; init; }

        public long RomSize { get; init; }

        public string Format { get; init; } = string.Empty;

        public int Normalized { get; init; }
    }
}
