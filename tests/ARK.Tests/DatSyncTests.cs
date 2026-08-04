using System.Net;
using ARK.Core.Dat;
using ARK.Core.Instances;

namespace ARK.Tests;

public sealed class DatSyncTests : IDisposable
{
    private readonly string _root;
    private readonly InstancePaths _paths;
    private readonly DatCatalog _catalog;

    public DatSyncTests()
    {
        _root = TempRoot.Create();
        _paths = new InstancePaths("dat-sync-tests", _root);
        Directory.CreateDirectory(_paths.Db);
        _catalog = new DatCatalog(_paths);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 13 — sync fetches a source, then the second run is served from cache with no network call.
    [Fact]
    public async Task Sync_fetches_then_serves_the_second_run_from_cache()
    {
        var handler = new CountingHandler(TestFixtures.Read("redump-psx.dat"));
        using var http = new HttpClient(handler);
        var sync = new DatSyncService(_catalog, http);
        var source = new DatSourceDefinition
        {
            Name = "Redump - PlayStation",
            System = "psx",
            Url = "http://redump.example/psx.dat"
        };

        var first = await sync.SyncAsync([source]);
        Assert.Equal(1, first.Fetched);
        Assert.Equal(1, handler.Requests);

        var second = await sync.SyncAsync([source]);
        Assert.Equal(1, second.Skipped);
        Assert.Equal(1, handler.Requests); // no additional network call on the cached run
    }

    // A failed fetch is recorded and does not abort the remaining sources.
    [Fact]
    public async Task Sync_records_a_failure_without_aborting_the_run()
    {
        using var http = new HttpClient(new FailingHandler());
        var sync = new DatSyncService(_catalog, http);
        var source = new DatSourceDefinition { Name = "Broken", System = "psx", Url = "http://broken.example/x.dat" };

        var summary = await sync.SyncAsync([source]);

        Assert.Equal(1, summary.Failed);
        Assert.Equal(0, summary.Fetched);
    }

    // The live-run defect. Redump serves its ordinary HTML page for the 21 of 79 systems that
    // publish no DAT, and the XML parser threw XmlException — which the catch filter did not name,
    // so a 79-source sync died on source 4 with a stack trace. The previous failure test passed
    // throughout, because it threw HttpRequestException, a type the filter already covered.
    [Fact]
    public async Task An_html_error_page_fails_one_source_and_the_run_continues()
    {
        const string html = """
            <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN">
            <html><body><p>Redump.org &bull; no datfile for this system</p></body></html>
            """;

        using var http = new HttpClient(new ScriptedHandler(new Dictionary<string, string>
        {
            ["http://redump.example/nothing.dat"] = html,
            ["http://redump.example/psx.dat"] = TestFixtures.Read("redump-psx.dat"),
        }));

        var summary = await new DatSyncService(_catalog, http).SyncAsync(
        [
            new DatSourceDefinition { Name = "No DAT", System = "psx", Url = "http://redump.example/nothing.dat" },
            new DatSourceDefinition { Name = "Redump - PlayStation", System = "psx", Url = "http://redump.example/psx.dat" },
        ]);

        Assert.Equal(1, summary.Failed);

        // The source after the failure still ran. That is the whole contract.
        Assert.Equal(1, summary.Fetched);

        var failure = Assert.Single(summary.Results, result => result.Outcome == SyncOutcome.Failed);
        Assert.Contains("HTML page", failure.Detail!, StringComparison.Ordinal);
        Assert.Contains("publishes no DAT", failure.Detail!, StringComparison.Ordinal);
    }

    // Any unlisted exception type must be recorded, never thrown. An enumerated catch filter can
    // only ever cover the failures already seen.
    [Fact]
    public async Task An_unexpected_exception_type_is_recorded_rather_than_thrown()
    {
        using var http = new HttpClient(new ThrowingHandler(new InvalidOperationException("something nobody listed")));

        var summary = await new DatSyncService(_catalog, http).SyncAsync(
            [new DatSourceDefinition { Name = "Surprising", System = "psx", Url = "http://x.example/x.dat" }]);

        Assert.Equal(1, summary.Failed);
        Assert.Contains("something nobody listed", Assert.Single(summary.Results).Detail!, StringComparison.Ordinal);
    }

    // Cancellation is the one exception that must still propagate: the user asked the run to stop,
    // and recording it as 79 individual failures would be a lie about what happened.
    [Fact]
    public async Task Cancellation_ends_the_run_rather_than_being_recorded_as_failures()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var http = new HttpClient(new CountingHandler(TestFixtures.Read("redump-psx.dat")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DatSyncService(_catalog, http).SyncAsync(
                [new DatSourceDefinition { Name = "Any", System = "psx", Url = "http://x.example/x.dat" }],
                force: false,
                cancellationToken: cts.Token));
    }

    private sealed class ScriptedHandler(Dictionary<string, string> byUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(byUrl[request.RequestUri!.ToString()]),
            });
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed class CountingHandler(string payload) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("network unavailable");
    }
}
