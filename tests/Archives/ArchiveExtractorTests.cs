using ARK.Core.Archives;
using System.IO.Compression;

namespace ARK.Tests.Archives;

public class ArchiveExtractorTests
{
    [Fact]
    public void Extract_ZipArchive_Succeeds()
    {
        var temp = Directory.CreateTempSubdirectory();
        var root = temp.FullName;
        var archivePath = Path.Combine(root, "sample.zip");

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("hello.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.WriteLine("hello world");
        }

        var destination = Path.Combine(root, "out");
        var result = ArchiveExtractor.Extract(archivePath, destination);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(destination, "hello.txt")));
    }
}
