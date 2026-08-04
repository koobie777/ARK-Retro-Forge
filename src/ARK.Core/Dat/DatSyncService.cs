namespace ARK.Core.Dat;

/// <summary>What happened to one source during a sync.</summary>
public enum SyncOutcome
{
    /// <summary>Downloaded and indexed.</summary>
    Fetched,

    /// <summary>Already present in the catalog; not refetched.</summary>
    Skipped,

    /// <summary>Download or parse failed; other sources continue.</summary>
    Failed
}

/// <summary>The result of syncing one source.</summary>
public sealed record DatSyncResult(string SourceName, SyncOutcome Outcome, string? Detail);

/// <summary>The result of a sync run.</summary>
public sealed record DatSyncSummary(IReadOnlyList<DatSyncResult> Results)
{
    /// <summary>Number of sources downloaded.</summary>
    public int Fetched => Results.Count(result => result.Outcome == SyncOutcome.Fetched);

    /// <summary>Number of sources served from cache.</summary>
    public int Skipped => Results.Count(result => result.Outcome == SyncOutcome.Skipped);

    /// <summary>Number of sources that failed.</summary>
    public int Failed => Results.Count(result => result.Outcome == SyncOutcome.Failed);
}

/// <summary>
/// Syncs DAT sources that expose a direct URL (Redump's entries qualify). A source already present in
/// the catalog is served from cache — the second run makes no network call. The <see cref="HttpClient"/>
/// is injected so the fetch/cache behaviour is testable without a network.
/// </summary>
public sealed class DatSyncService
{
    private readonly DatCatalog _catalog;
    private readonly HttpClient _http;

    /// <summary>Creates a sync service over the catalog, using <paramref name="http"/> for downloads.</summary>
    public DatSyncService(DatCatalog catalog, HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(http);
        _catalog = catalog;
        _http = http;
    }

    /// <summary>
    /// Syncs the given sources. Each source already indexed from its URL is skipped without a network
    /// call unless <paramref name="force"/> is set. A failure on one source does not abort the rest.
    /// </summary>
    public async Task<DatSyncSummary> SyncAsync(
        IEnumerable<DatSourceDefinition> sources,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var results = new List<DatSyncResult>();
        foreach (var source in sources)
        {
            if (!force && _catalog.ContainsOrigin(source.Url))
            {
                results.Add(new DatSyncResult(source.Name, SyncOutcome.Skipped, "Already cached"));
                continue;
            }

            try
            {
                await using var stream = await _http.GetStreamAsync(source.Url, cancellationToken).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                buffer.Position = 0;

                var indexed = 0;
                foreach (var (_, dat) in DatReader.ReadDats(buffer))
                {
                    _catalog.Import(dat, source.System, source.Url);
                    indexed += dat.Entries.Count;
                }

                results.Add(new DatSyncResult(source.Name, SyncOutcome.Fetched, $"Indexed {indexed} entr{(indexed == 1 ? "y" : "ies")}"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The user asked to stop. Everything already indexed is committed; re-raise so the
                // run ends rather than being recorded as 79 individual failures.
                throw;
            }
            catch (Exception ex)
            {
                // Deliberately unfiltered. The contract above is that one bad source never aborts
                // the rest, and an exception list can only ever be as complete as the failures
                // already seen — a live run against Redump threw XmlException from an HTML error
                // page, which the previous filter did not name, and the whole 79-source sync died
                // on source 4 with a stack trace.
                results.Add(new DatSyncResult(source.Name, SyncOutcome.Failed, Describe(ex)));
            }
        }

        return new DatSyncSummary(results);
    }

    /// <summary>A one-line reason a user can act on, never a stack trace.</summary>
    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException http => $"download failed: {http.Message}",
        InvalidDataException invalid => invalid.Message,
        System.Xml.XmlException xml => $"response is not a readable DAT: {xml.Message}",
        _ => ex.Message,
    };
}
