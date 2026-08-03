using System.IO.Compression;
using ARK.Core.Hashing;
using ARK.Core.Instances;
using ARK.Core.Scanning;
using ARK.Core.Units;

namespace ARK.Tests;

/// <summary>
/// Phase 5 Part A and B: hashing the ROM rather than the archive, the cache, and format detection.
/// </summary>
public sealed class HashingTests : IDisposable
{
    private readonly string _root = TempRoot.Create();

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Gate 3. DAT hashes describe the ROM inside; zip compression is not deterministic, so hashing
    // the archive can never match no matter how correct the file is.
    [Fact]
    public void Hashes_the_rom_inside_the_zip_not_the_archive()
    {
        var rom = new byte[] { 0x80, 0x37, 0x12, 0x40, 1, 2, 3, 4 };
        var archive = WriteZip("Super Test 64 (USA).zip", "Super Test 64 (USA).z64", rom);

        var expected = Crc32Of(rom);
        var archiveBytes = File.ReadAllBytes(archive);

        var hasher = new RomHasher(new FileSystemReader(), new ArchiveInspector(new FileSystemReader(), new[] { ".zip" }));
        var result = hasher.Compute(Describe(archive));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(expected, result.Hash!.Crc32);
        Assert.NotEqual(Crc32Of(archiveBytes), result.Hash.Crc32);
        Assert.Equal(rom.Length, result.Hash.Size);

        // Nothing was unpacked next to the archive.
        Assert.Equal(new[] { archive }, Directory.GetFiles(_root));
    }

    [Fact]
    public void Hashes_a_bare_rom_with_no_archive()
    {
        var rom = new byte[] { 0x80, 0x37, 0x12, 0x40, 9, 9, 9, 9 };
        var path = Path.Combine(_root, "Bare (USA).z64");
        File.WriteAllBytes(path, rom);

        var hasher = new RomHasher(new FileSystemReader(), new ArchiveInspector(new FileSystemReader(), new[] { ".zip" }));
        var result = hasher.Compute(Describe(path));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(Crc32Of(rom), result.Hash!.Crc32);
    }

    // Gate 10. Extensions lie; magic bytes do not.
    [Theory]
    [InlineData(new byte[] { 0x80, 0x37, 0x12, 0x40 }, RomFormat.N64BigEndian, "BigEndian")]
    [InlineData(new byte[] { 0x37, 0x80, 0x40, 0x12 }, RomFormat.N64ByteSwapped, "ByteSwapped")]
    [InlineData(new byte[] { 0x40, 0x12, 0x37, 0x80 }, RomFormat.N64LittleEndian, "LittleEndian")]
    public void Format_is_detected_from_magic_bytes(byte[] magic, RomFormat expected, string qualifier)
    {
        var detection = RomFormatDetector.Detect(magic, 1024);

        Assert.Equal(expected, detection.Format);
        Assert.Equal(qualifier, detection.Qualifier);
    }

    // The mislabelled case stated explicitly: a byteswapped dump named .z64.
    [Fact]
    public void Mislabelled_extension_is_detected_from_content()
    {
        var rom = new byte[1024];
        new byte[] { 0x37, 0x80, 0x40, 0x12 }.CopyTo(rom, 0);
        var path = Path.Combine(_root, "Mislabelled (USA).z64");
        File.WriteAllBytes(path, rom);

        var hasher = new RomHasher(new FileSystemReader(), new ArchiveInspector(new FileSystemReader(), new[] { ".zip" }));
        var result = hasher.Compute(Describe(path));

        Assert.Equal(RomFormat.N64ByteSwapped, result.Hash!.Format.Format);
    }

    [Theory]
    [InlineData(0x00, RomFormat.NesINes)]
    [InlineData(0x08, RomFormat.NesNes20)]
    public void Nes_header_version_is_read_from_byte_seven(byte flags7, RomFormat expected)
    {
        var head = new byte[16];
        new byte[] { 0x4E, 0x45, 0x53, 0x1A }.CopyTo(head, 0);
        head[7] = flags7;

        Assert.Equal(expected, RomFormatDetector.Detect(head, 40976).Format);
    }

    // SNES copier headers have no signature — the tell is the size.
    [Theory]
    [InlineData(524800)] // 512 KiB + 512
    [InlineData(1049088)] // 1 MiB + 512
    public void Snes_copier_header_is_detected_from_size(long size)
    {
        var detection = RomFormatDetector.Detect(new byte[16], size);

        Assert.Equal(RomFormat.SnesHeadered, detection.Format);
        Assert.Equal("Headered", detection.Qualifier);
    }

    // The absence of a copier header is not a detection. Claiming it labelled all 592 Game Boy
    // ROMs on the reference drive as headerless SNES, because almost every ROM of every system is
    // a whole number of KiB.
    [Theory]
    [InlineData(524288)] // 512 KiB — an ordinary ROM of some system
    [InlineData(32768)] // 32 KiB Game Boy title
    [InlineData(4194304)] // 4 MiB
    public void Whole_kib_size_alone_claims_no_format(long size)
    {
        var detection = RomFormatDetector.Detect(new byte[16], size);

        Assert.Equal(RomFormat.Unknown, detection.Format);
        Assert.Null(detection.Qualifier);
    }

    // Gate 11.
    [Fact]
    public void Detected_format_contradicting_the_folder_qualifier_is_reported()
    {
        var byteSwapped = RomFormatDetector.Detect(new byte[] { 0x37, 0x80, 0x40, 0x12 }, 1024);

        Assert.True(RomFormatDetector.Contradicts(byteSwapped, "BigEndian"));
        Assert.False(RomFormatDetector.Contradicts(byteSwapped, "ByteSwapped"));

        // Nothing to contradict when the folder declares nothing.
        Assert.False(RomFormatDetector.Contradicts(byteSwapped, null));
    }

    // Gate 12. The transform produces a correct hash and touches no file.
    [Fact]
    public void In_memory_transform_produces_a_correct_hash_and_writes_nothing()
    {
        var bigEndian = new byte[] { 0x80, 0x37, 0x12, 0x40, 0xAA, 0xBB, 0xCC, 0xDD };
        var byteSwapped = new byte[] { 0x37, 0x80, 0x40, 0x12, 0xBB, 0xAA, 0xDD, 0xCC };

        var path = Path.Combine(_root, "Swapped (USA).v64");
        File.WriteAllBytes(path, byteSwapped);
        var before = File.ReadAllBytes(path);

        var hasher = new RomHasher(new FileSystemReader(), new ArchiveInspector(new FileSystemReader(), new[] { ".zip" }));
        var result = hasher.Compute(Describe(path), HashPurpose.Verification, normalize: true);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Hash!.Normalized);
        Assert.Equal(Crc32Of(bigEndian), result.Hash.Crc32);

        // The file on disk is untouched.
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(_root));
    }

    [Fact]
    public void Snes_copier_header_is_skipped_in_memory_only()
    {
        var body = new byte[1024];
        Random.Shared.NextBytes(body);
        var headered = new byte[512 + body.Length];
        body.CopyTo(headered, 512);

        var path = Path.Combine(_root, "Headered (USA).sfc");
        File.WriteAllBytes(path, headered);

        var hasher = new RomHasher(new FileSystemReader(), new ArchiveInspector(new FileSystemReader(), new[] { ".zip" }));
        var result = hasher.Compute(Describe(path), HashPurpose.Verification, normalize: true);

        Assert.Equal(Crc32Of(body), result.Hash!.Crc32);
        Assert.Equal(body.Length, result.Hash.Size);
        Assert.Equal(headered, File.ReadAllBytes(path));
    }

    // Identification needs SHA1 to settle 32-bit collisions across 1.5M entries; verification,
    // comparing against one known value, does not.
    [Fact]
    public void Sha1_is_computed_only_for_identification()
    {
        var path = Path.Combine(_root, "Sample (USA).nes");
        File.WriteAllBytes(path, new byte[] { 0x4E, 0x45, 0x53, 0x1A, 1, 2, 3, 4 });

        var hasher = new RomHasher(new FileSystemReader(), new ArchiveInspector(new FileSystemReader(), new[] { ".zip" }));

        Assert.Null(hasher.Compute(Describe(path), HashPurpose.Verification).Hash!.Sha1);
        Assert.NotNull(hasher.Compute(Describe(path), HashPurpose.Identification).Hash!.Sha1);
    }

    // Gates 4 and 5.
    [Fact]
    public void Cache_hits_on_an_unchanged_file_and_invalidates_when_mtime_moves()
    {
        var paths = new InstancePaths("hash-cache", _root);
        Directory.CreateDirectory(paths.Db);
        var cache = new HashCache(paths);

        var file = Describe(WriteZip("Game (USA).zip", "Game (USA).z64", new byte[] { 0x80, 0x37, 0x12, 0x40, 5 }));
        var hash = new RomHash("deadbeef", null, 5, RomFormatDetection.None, false);

        cache.Store(file.FullPath, file.Size, file.ModifiedUtc, hash);

        var hit = cache.TryGet(file.FullPath, file.Size, file.ModifiedUtc);
        Assert.NotNull(hit);
        Assert.Equal("deadbeef", hit!.Crc32);
        Assert.Equal(1, cache.Hits);

        // A changed timestamp is a miss, never a stale hit.
        Assert.Null(cache.TryGet(file.FullPath, file.Size, file.ModifiedUtc.AddSeconds(1)));
        Assert.Null(cache.TryGet(file.FullPath, file.Size + 1, file.ModifiedUtc));

        cache.Close();
    }

    // Formats are cached by NAME, never by enum ordinal. An ordinal is silently reinterpreted the
    // moment the enum changes — removing one member relabelled 592 cached Game Boy ROMs as NES.
    [Fact]
    public void Cached_format_survives_the_enum_changing()
    {
        var paths = new InstancePaths("format-round-trip", _root);
        Directory.CreateDirectory(paths.Db);
        var cache = new HashCache(paths);

        var detection = new RomFormatDetection(RomFormat.N64ByteSwapped, "ByteSwapped", "test");
        cache.Store(@"D:\a.zip", 10, DateTimeOffset.UnixEpoch, new RomHash("aaaa", null, 10, detection, false));

        var restored = cache.TryGet(@"D:\a.zip", 10, DateTimeOffset.UnixEpoch);
        Assert.Equal(RomFormat.N64ByteSwapped, restored!.Format.Format);
        Assert.Equal("ByteSwapped", restored.Format.Qualifier);

        // A name this build no longer knows degrades to Unknown, and the CRC32 still survives —
        // which is the part the cache exists to preserve.
        cache.Close();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={paths.HashCacheDatabase};Pooling=False"))
        {
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE hashes SET format = 'SomeRetiredFormat';";
            update.ExecuteNonQuery();
        }

        var reopened = new HashCache(paths);
        var degraded = reopened.TryGet(@"D:\a.zip", 10, DateTimeOffset.UnixEpoch);
        Assert.Equal(RomFormat.Unknown, degraded!.Format.Format);
        Assert.Equal("aaaa", degraded.Crc32);
        reopened.Close();
    }

    // Gate 6. Entries are committed as produced, so an interrupted run keeps its work.
    [Fact]
    public void Cache_entries_survive_a_connection_being_dropped_mid_run()
    {
        var paths = new InstancePaths("hash-cache-durability", _root);
        Directory.CreateDirectory(paths.Db);

        var first = new HashCache(paths);
        first.Store(@"D:\a.zip", 10, DateTimeOffset.UnixEpoch, new RomHash("aaaa", null, 10, RomFormatDetection.None, false));
        first.Store(@"D:\b.zip", 20, DateTimeOffset.UnixEpoch, new RomHash("bbbb", null, 20, RomFormatDetection.None, false));
        first.Close(); // stands in for the process being interrupted

        var reopened = new HashCache(paths);
        Assert.Equal(2, reopened.Count());
        Assert.Equal("aaaa", reopened.TryGet(@"D:\a.zip", 10, DateTimeOffset.UnixEpoch)!.Crc32);
        reopened.Close();
    }

    private string WriteZip(string archiveName, string entryName, byte[] content)
    {
        var path = Path.Combine(_root, archiveName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var stream = archive.CreateEntry(entryName).Open();
        stream.Write(content);
        return path;
    }

    private static FileEntry Describe(string path) => new FileSystemReader().Describe(path)!;

    private static string Crc32Of(byte[] bytes)
    {
        using var crc = new Crc32Hasher();
        crc.Append(bytes);
        return Convert.ToHexString(crc.GetHashAndReset()).ToLowerInvariant();
    }
}
