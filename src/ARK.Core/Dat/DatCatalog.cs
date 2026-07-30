using ARK.Core.Instances;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ARK.Core.Dat;

/// <summary>How a name candidate matched, best first.</summary>
public enum NameMatch
{
    /// <summary>The queried name equals the entry's ROM or game name.</summary>
    Exact = 0,

    /// <summary>The names match once normalized (case, spacing, extension).</summary>
    Normalized = 1,

    /// <summary>The queried name is a substring of the entry's name.</summary>
    Partial = 2
}

/// <summary>A single indexed DAT entry joined to its owning catalog. (Init props: Dapper-materialized.)</summary>
public sealed record CatalogEntry
{
    public string? System { get; init; }
    public string DatName { get; init; } = string.Empty;
    public string? GameName { get; init; }
    public string RomName { get; init; } = string.Empty;
    public long? Size { get; init; }
    public string? Crc32 { get; init; }
    public string? Md5 { get; init; }
    public string? Sha1 { get; init; }
}

/// <summary>A ranked name-search result.</summary>
public sealed record NameCandidate(CatalogEntry Entry, NameMatch Match);

/// <summary>Coverage summary for one indexed DAT. (Init props: Dapper-materialized.)</summary>
public sealed record DatCoverage
{
    public string DatName { get; init; } = string.Empty;
    public string? System { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public string? Date { get; init; }
    public string? Author { get; init; }
    public int EntryCount { get; init; }
    public string ImportedUtc { get; init; } = string.Empty;
}

/// <summary>The outcome of importing one DAT into the catalog.</summary>
public sealed record DatImportResult(string DatName, string? System, int EntryCount);

/// <summary>
/// The per-instance SQLite catalog of indexed DAT entries. Writes go through the SQLite driver, not
/// through <c>File.*</c>/<c>Directory.*</c> calls, so the Executor-only filesystem rule is not
/// engaged; the database lives under the instance's provisioned <c>db/</c> directory, resolved via
/// <see cref="InstancePaths"/>. Import is idempotent (keyed on the DAT's header name) and reads never
/// create the database.
/// </summary>
public sealed class DatCatalog
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS catalogs (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            dat_name     TEXT NOT NULL UNIQUE COLLATE NOCASE,
            system       TEXT COLLATE NOCASE,
            description  TEXT,
            version      TEXT,
            date         TEXT,
            author       TEXT,
            origin       TEXT,
            imported_utc TEXT NOT NULL,
            entry_count  INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS entries (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            catalog_id INTEGER NOT NULL REFERENCES catalogs(id) ON DELETE CASCADE,
            game_name  TEXT,
            rom_name   TEXT,
            size       INTEGER,
            crc32      TEXT COLLATE NOCASE,
            md5        TEXT COLLATE NOCASE,
            sha1       TEXT COLLATE NOCASE
        );
        CREATE INDEX IF NOT EXISTS ix_entries_crc32 ON entries(crc32);
        CREATE INDEX IF NOT EXISTS ix_entries_md5   ON entries(md5);
        CREATE INDEX IF NOT EXISTS ix_entries_sha1  ON entries(sha1);
        CREATE INDEX IF NOT EXISTS ix_entries_rom   ON entries(rom_name);
        CREATE INDEX IF NOT EXISTS ix_entries_size  ON entries(size);
        """;

    private const string EntrySelect = """
        SELECT c.system AS System, c.dat_name AS DatName, e.game_name AS GameName, e.rom_name AS RomName,
               e.size AS Size, e.crc32 AS Crc32, e.md5 AS Md5, e.sha1 AS Sha1
        FROM entries e
        JOIN catalogs c ON c.id = e.catalog_id
        """;

    private readonly string _databasePath;

    /// <summary>Creates a catalog backed by the instance's <c>db/catalog.db</c>.</summary>
    public DatCatalog(InstancePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _databasePath = paths.CatalogDatabase;
    }

    /// <summary>
    /// Indexes <paramref name="dat"/> under <paramref name="system"/>. Idempotent: re-importing a DAT
    /// with the same header name replaces the previous rows rather than duplicating them.
    /// </summary>
    public DatImportResult Import(LogiqxDat dat, string? system, string origin)
    {
        ArgumentNullException.ThrowIfNull(dat);
        var datName = dat.Header.Name is { Length: > 0 } name ? name : origin;

        using var connection = OpenWritable();
        using var transaction = connection.BeginTransaction();

        connection.Execute("DELETE FROM catalogs WHERE dat_name = @datName;", new { datName }, transaction);

        connection.Execute(
            """
            INSERT INTO catalogs (dat_name, system, description, version, date, author, origin, imported_utc, entry_count)
            VALUES (@datName, @system, @description, @version, @date, @author, @origin, @importedUtc, @entryCount);
            """,
            new
            {
                datName,
                system,
                description = dat.Header.Description,
                version = dat.Header.Version,
                date = dat.Header.Date,
                author = dat.Header.Author,
                origin,
                importedUtc = DateTime.UtcNow.ToString("o"),
                entryCount = dat.Entries.Count
            },
            transaction);

        var catalogId = connection.ExecuteScalar<long>("SELECT last_insert_rowid();", transaction: transaction);

        foreach (var entry in dat.Entries)
        {
            connection.Execute(
                """
                INSERT INTO entries (catalog_id, game_name, rom_name, size, crc32, md5, sha1)
                VALUES (@catalogId, @GameName, @RomName, @Size, @Crc32, @Md5, @Sha1);
                """,
                new { catalogId, entry.GameName, entry.RomName, entry.Size, entry.Crc32, entry.Md5, entry.Sha1 },
                transaction);
        }

        transaction.Commit();
        return new DatImportResult(datName, system, dat.Entries.Count);
    }

    /// <summary>Exact hash lookup across CRC32/MD5/SHA1. Returns one entry or none — never a guess.</summary>
    public CatalogEntry? FindByHash(string hash)
    {
        var normalized = hash?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        using var connection = OpenReadOnly();
        return connection?.QueryFirstOrDefault<CatalogEntry>(
            $"{EntrySelect} WHERE e.crc32 = @normalized OR e.md5 = @normalized OR e.sha1 = @normalized LIMIT 1;",
            new { normalized });
    }

    /// <summary>Ranked name search: exact, then normalized, then partial.</summary>
    public IReadOnlyList<NameCandidate> FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        using var connection = OpenReadOnly();
        if (connection is null)
        {
            return [];
        }

        var query = name.Trim();
        var like = $"%{query}%";
        var rows = connection.Query<CatalogEntry>(
            $"{EntrySelect} WHERE e.rom_name LIKE @like OR e.game_name LIKE @like;",
            new { like });

        var normalizedQuery = NormalizeName(query);
        return rows
            .Select(row => new NameCandidate(row, RankOf(row, query, normalizedQuery)))
            .OrderBy(candidate => candidate.Match)
            .ThenBy(candidate => candidate.Entry.RomName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Per-DAT coverage. Empty when nothing has been indexed.</summary>
    public IReadOnlyList<DatCoverage> Coverage()
    {
        using var connection = OpenReadOnly();
        return connection?.Query<DatCoverage>(
            """
            SELECT dat_name AS DatName, system AS System, description AS Description, version AS Version,
                   date AS Date, author AS Author, entry_count AS EntryCount, imported_utc AS ImportedUtc
            FROM catalogs
            ORDER BY system, dat_name;
            """).ToList() ?? [];
    }

    /// <summary>True if a catalog was already imported from <paramref name="origin"/> (sync cache check).</summary>
    public bool ContainsOrigin(string origin)
    {
        using var connection = OpenReadOnly();
        return connection is not null
            && connection.ExecuteScalar<long>("SELECT COUNT(1) FROM catalogs WHERE origin = @origin;", new { origin }) > 0;
    }

    private SqliteConnection OpenWritable()
    {
        // Pooling is disabled so the database file handle is released as soon as the connection is
        // disposed — important for a per-instance file that may be deleted or moved.
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON;");
        connection.Execute(SchemaSql);
        return connection;
    }

    private SqliteConnection? OpenReadOnly()
    {
        if (!File.Exists(_databasePath))
        {
            return null;
        }

        var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        return connection;
    }

    private static NameMatch RankOf(CatalogEntry row, string query, string normalizedQuery)
    {
        if (string.Equals(row.RomName, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.GameName, query, StringComparison.OrdinalIgnoreCase))
        {
            return NameMatch.Exact;
        }

        if (NormalizeName(row.RomName) == normalizedQuery ||
            (row.GameName is not null && NormalizeName(row.GameName) == normalizedQuery))
        {
            return NameMatch.Normalized;
        }

        return NameMatch.Partial;
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        var dot = text.LastIndexOf('.');
        if (dot > 0 && text.Length - dot <= 5)
        {
            text = text[..dot];
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }
}
