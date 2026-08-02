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

    /// <summary>Format qualifier of the owning DAT variant, e.g. <c>Headered</c>. Null when unqualified.</summary>
    public string? Qualifier { get; init; }

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

    /// <summary>Format qualifier of this DAT variant, e.g. <c>Headered</c>. Null when unqualified.</summary>
    public string? Qualifier { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public string? Date { get; init; }
    public string? Author { get; init; }
    public int EntryCount { get; init; }
    public string ImportedUtc { get; init; } = string.Empty;
}

/// <summary>The outcome of importing one DAT into the catalog.</summary>
/// <param name="DatName">DAT header name, the idempotency key.</param>
/// <param name="System">Resolved system code, or null when unrecognized.</param>
/// <param name="Qualifier">Declared format qualifier, or null when the DAT name carried none.</param>
/// <param name="EntryCount">Entries indexed.</param>
public sealed record DatImportResult(string DatName, string? System, string? Qualifier, int EntryCount)
{
    /// <summary>Label for the resolved variant, e.g. <c>nes (Headered)</c>, or <c>(unrecognized)</c>.</summary>
    public string SystemLabel => System is null
        ? "(unrecognized)"
        : Qualifier is null ? System : $"{System} ({Qualifier})";
}

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
            qualifier    TEXT COLLATE NOCASE,
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
        SELECT c.system AS System, c.qualifier AS Qualifier, c.dat_name AS DatName, e.game_name AS GameName,
               e.rom_name AS RomName, e.size AS Size, e.crc32 AS Crc32, e.md5 AS Md5, e.sha1 AS Sha1
        FROM entries e
        JOIN catalogs c ON c.id = e.catalog_id
        """;

    private readonly string _databasePath;
    private bool _migrated;

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
    public DatImportResult Import(LogiqxDat dat, string? system, string origin) =>
        Import(dat, system, null, origin);

    /// <summary>
    /// Indexes <paramref name="dat"/> under a specific (system, qualifier) variant. Idempotent:
    /// re-importing a DAT with the same header name replaces the previous rows rather than
    /// duplicating them.
    /// </summary>
    public DatImportResult Import(LogiqxDat dat, string? system, string? qualifier, string origin)
    {
        ArgumentNullException.ThrowIfNull(dat);
        var datName = dat.Header.Name is { Length: > 0 } name ? name : origin;

        using var connection = OpenWritable();
        using var transaction = connection.BeginTransaction();

        connection.Execute("DELETE FROM catalogs WHERE dat_name = @datName;", new { datName }, transaction);

        connection.Execute(
            """
            INSERT INTO catalogs (dat_name, system, qualifier, description, version, date, author, origin, imported_utc, entry_count)
            VALUES (@datName, @system, @qualifier, @description, @version, @date, @author, @origin, @importedUtc, @entryCount);
            """,
            new
            {
                datName,
                system,
                qualifier,
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
        return new DatImportResult(datName, system, qualifier, dat.Entries.Count);
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

    /// <summary>
    /// Every indexed entry, optionally narrowed to one system. Used to build an in-memory
    /// token-set index for scanning: identifying 10,000 files one SQL query at a time would be
    /// 10,000 round trips, and name identity is a token-set comparison rather than anything SQL
    /// can express.
    /// </summary>
    public IReadOnlyList<CatalogEntry> AllEntries(string? system = null)
    {
        using var connection = OpenReadOnly();
        if (connection is null)
        {
            return [];
        }

        return system is { Length: > 0 }
            ? connection.Query<CatalogEntry>($"{EntrySelect} WHERE c.system = @system;", new { system }).ToList()
            : connection.Query<CatalogEntry>($"{EntrySelect};").ToList();
    }

    /// <summary>
    /// Every entry belonging to one named DAT. Identification is scoped to a single DAT, so this
    /// is the query that backs it: a Redump disc image and a PSN re-release share a title and are
    /// entirely different artifacts, and comparing across DATs answers the wrong question.
    /// </summary>
    public IReadOnlyList<CatalogEntry> EntriesForDat(string datName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datName);

        using var connection = OpenReadOnly();
        return connection?
            .Query<CatalogEntry>($"{EntrySelect} WHERE c.dat_name = @datName;", new { datName })
            .ToList() ?? [];
    }

    /// <summary>
    /// Entries belonging to one <b>(system, qualifier)</b> variant. <c>(Headered)</c> and
    /// <c>(Headerless)</c> cover the same games with different hashes, so a lookup that cannot
    /// name the variant cannot give a correct answer.
    /// </summary>
    /// <param name="system">System code.</param>
    /// <param name="qualifier">
    /// Declared qualifier, or null for the unqualified variant. Null is matched exactly, not
    /// treated as "any" — the unqualified DAT is its own set.
    /// </param>
    public IReadOnlyList<CatalogEntry> EntriesFor(string system, string? qualifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);

        using var connection = OpenReadOnly();
        if (connection is null)
        {
            return [];
        }

        var predicate = qualifier is null ? "c.qualifier IS NULL" : "c.qualifier = @qualifier";
        return connection
            .Query<CatalogEntry>($"{EntrySelect} WHERE c.system = @system AND {predicate};", new { system, qualifier })
            .ToList();
    }

    /// <summary>
    /// Per-DAT coverage, optionally filtered. Filtering lives here rather than in the caller
    /// because the real catalog holds over a million entries across roughly 200 DATs, and pulling
    /// all of it back to discard most of it is not a rendering concern.
    /// </summary>
    /// <param name="system">Limit to one system code, or null for all.</param>
    /// <param name="recognized">
    /// True for DATs that resolved to a system, false for those that did not, null for both.
    /// </param>
    public IReadOnlyList<DatCoverage> Coverage(string? system = null, bool? recognized = null)
    {
        using var connection = OpenReadOnly();
        if (connection is null)
        {
            return [];
        }

        var filters = new List<string>();
        if (system is { Length: > 0 })
        {
            filters.Add("system = @system");
        }

        if (recognized is not null)
        {
            filters.Add(recognized.Value ? "system IS NOT NULL" : "system IS NULL");
        }

        var where = filters.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", filters);

        return connection.Query<DatCoverage>(
            $"""
            SELECT dat_name AS DatName, system AS System, qualifier AS Qualifier, description AS Description,
                   version AS Version, date AS Date, author AS Author, entry_count AS EntryCount,
                   imported_utc AS ImportedUtc
            FROM catalogs
            {where}
            ORDER BY system IS NULL, system, qualifier, dat_name;
            """,
            new { system }).ToList();
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
        Migrate();

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

        Migrate();

        var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Brings an existing catalog up to the current schema. <c>CREATE TABLE IF NOT EXISTS</c> does
    /// not add columns to a table that already exists, and a real catalog holds over a million
    /// entries across roughly 200 DATs — re-indexing all of it to gain one nullable column would
    /// be a poor trade. Runs once per instance and only when the database is already there, so
    /// reads still never create it.
    /// </summary>
    private void Migrate()
    {
        if (_migrated)
        {
            return;
        }

        _migrated = true;

        if (!File.Exists(_databasePath))
        {
            return;
        }

        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();

        var columns = connection.Query<string>("SELECT name FROM pragma_table_info('catalogs');").ToList();
        if (columns.Count > 0 && !columns.Contains("qualifier", StringComparer.OrdinalIgnoreCase))
        {
            // Existing rows take a null qualifier. DATs that were unrecognized before this change
            // stay unrecognized until re-imported, and import is idempotent, so re-import is safe.
            connection.Execute("ALTER TABLE catalogs ADD COLUMN qualifier TEXT COLLATE NOCASE;");
        }
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
