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
