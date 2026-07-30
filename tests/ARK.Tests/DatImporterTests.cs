using System.IO.Compression;
using ARK.Core.Dat;
using ARK.Core.Instances;
using ARK.Core.Systems;

namespace ARK.Tests;

public sealed class DatImporterTests : IDisposable
{
    private readonly string _root;
    private readonly InstancePaths _paths;
    private readonly DatImporter _importer;

    public DatImporterTests()
    {
        _root = TempRoot.Create();
        _paths = new InstancePaths("dat-import-tests", _root);
        Directory.CreateDirectory(_paths.Db);
        var systems = SystemRegistry.FromDefinitions(
        [
            new SystemDefinition { Code = "n64", DisplayName = "Nintendo 64", Aliases = ["Nintendo - Nintendo 64"] }
        ]);
        _importer = new DatImporter(new DatCatalog(_paths), systems);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 12 — import ingests an archive containing multiple DATs.
    [Fact]
    public void Import_ingests_an_archive_of_multiple_dats()
    {
        var archivePath = Path.Combine(_root, "daily.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "nointro-n64.dat", TestFixtures.Read("nointro-n64.dat"));
            AddEntry(zip, "redump-psx.dat", TestFixtures.Read("redump-psx.dat"));
        }

        var summary = _importer.Import(archivePath);

        Assert.Equal(2, summary.DatsImported);
        Assert.Equal(8, summary.EntriesIndexed); // 3 + 5
        Assert.Contains(summary.Results, result => result.DatName == "Nintendo - Nintendo 64" && result.System == "n64");
        Assert.Contains(summary.Results, result => result.DatName == "Sony - PlayStation");
    }

    // A single DAT infers its system from the header via the registry.
    [Fact]
    public void Import_of_a_single_dat_infers_the_system()
    {
        var path = Path.Combine(_root, "n64.dat");
        File.WriteAllText(path, TestFixtures.Read("nointro-n64.dat"));

        var summary = _importer.Import(path);

        Assert.Equal(1, summary.DatsImported);
        Assert.Equal("n64", summary.Results[0].System);
    }

    // A directory of DATs imports every DAT it contains.
    [Fact]
    public void Import_of_a_directory_imports_each_dat()
    {
        var directory = Path.Combine(_root, "dats");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "a.dat"), TestFixtures.Read("nointro-n64.dat"));
        File.WriteAllText(Path.Combine(directory, "b.dat"), TestFixtures.Read("redump-psx.dat"));

        var summary = _importer.Import(directory);

        Assert.Equal(2, summary.DatsImported);
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
