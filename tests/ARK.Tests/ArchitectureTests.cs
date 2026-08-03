namespace ARK.Tests;

/// <summary>
/// Enforces the structural invariant from Phase 0: the <c>Executor</c> is the only type in
/// production code permitted to mutate the filesystem. This makes DRY-RUN safety and
/// reversibility impossible to violate by accident rather than matters of discipline.
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] ForbiddenCalls =
    {
        "File.Move(",
        "File.Delete(",
        "File.WriteAllText(",
        "Directory.CreateDirectory(",
    };

    [Fact]
    public void Only_the_executor_mutates_the_filesystem()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (Path.GetFileName(file).Equals("Executor.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var call in ForbiddenCalls)
            {
                if (text.Contains(call, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} contains '{call}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Filesystem-mutating calls are permitted only in Executor.cs. Offenders:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // SharpCompress 0.41.0 carries an unpatched zip-slip path traversal in
    // IArchive.WriteToDirectory() (CVE-2026-44788 / GHSA-6c8g-7p36-r338): a crafted archive
    // escapes the target root, escalating to arbitrary file writes on TAR via symlink chaining.
    // Every release through 0.47.4 is affected, so there is no version bump to take — banning the
    // method is the mitigation. ARK never needs it: scan reads the entry list and hashing streams
    // a single entry. If extraction ever becomes a verb it must be hand-rolled with per-entry path
    // validation against the target root, never routed through this method.
    [Fact]
    public void SharpCompress_WriteToDirectory_is_never_referenced()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (File.ReadAllText(file).Contains("WriteToDirectory(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "SharpCompress IArchive.WriteToDirectory() is banned (unpatched zip-slip, GHSA-6c8g-7p36-r338). " +
            "Extraction must be hand-rolled with path validation. Offenders:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // Path composition (instance/tools/dat layout) is centralized in the path-resolution types so
    // that "nothing composes paths outside the resolver" is enforceable. Business and diagnostic
    // code takes composed paths from the resolver rather than building them inline.
    private static readonly string[] PathCompositionCalls =
    {
        "Path.Combine(",
        "Path.Join(",
    };

    private static readonly string[] PathResolverFiles =
    {
        "InstancePaths.cs",
        "ToolLocator.cs",
        "QuarantinePaths.cs",
    };

    [Fact]
    public void Paths_are_composed_only_in_the_resolver_types()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (PathResolverFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var call in PathCompositionCalls)
            {
                if (text.Contains(call, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} contains '{call}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Path composition is permitted only in the resolver types (" + string.Join(", ", PathResolverFiles) + "). Offenders:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // Gate 9: the unreachable ToolMissing branch is gone. Medical Bay reports status; it reaches no
    // fatal verdict, so no exit-code branch keyed on tool presence survives anywhere in production code.
    [Fact]
    public void No_tool_missing_verdict_branch_remains()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (File.ReadAllText(file).Contains("ToolMissing", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "The ToolMissing verdict branch must not exist. Offenders:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // Phase 3 gate 14: naming is pure string work. Keeping every file read out of Core/Naming is
    // what makes the tokenizer testable at string speed against a 9,363-name corpus, and it is
    // why the one type that does read config lives in Core/Configuration instead.
    private static readonly string[] IoCalls =
    {
        "File.",
        "Directory.",
        "FileInfo",
        "DirectoryInfo",
        "StreamReader",
        "StreamWriter",
        "Path.",
    };

    [Fact]
    public void Naming_performs_no_io()
    {
        var namingDirectory = Path.Combine(FindSourceRoot(), "ARK.Core", "Naming");
        Assert.True(Directory.Exists(namingDirectory), $"Expected {namingDirectory} to exist.");

        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(namingDirectory))
        {
            var text = File.ReadAllText(file);
            foreach (var call in IoCalls)
            {
                if (text.Contains(call, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} contains '{call}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Core/Naming is pure string work and must perform no I/O. Offenders:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // Phase 4 gate 13: file metadata is reached through IFileSystemReader, so every type in the
    // scan pipeline is pure and the 21,095-file reference drive runs with no disk involved.
    // FileHasher is the pre-existing exception: hashing genuinely needs the file itself.
    private static readonly string[] FileMetadataTypes =
    {
        "FileInfo",
        "DirectoryInfo",
    };

    private static readonly string[] FilesystemAccessFiles =
    {
        "FileSystemReader.cs",
        "FileHasher.cs",
        "InstancePaths.cs",
    };

    [Fact]
    public void File_metadata_types_are_touched_only_by_the_filesystem_reader()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (FilesystemAccessFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var type in FileMetadataTypes)
            {
                if (text.Contains(type, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} references '{type}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "FileInfo/DirectoryInfo are permitted only in (" + string.Join(", ", FilesystemAccessFiles) +
            "). Everything else takes file metadata from IFileSystemReader. Offenders:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // Phase 6 gate 3. An enum written as its ordinal is silently reinterpreted the moment the enum
    // changes. The hash cache did exactly that and relabelled 592 rows; in the journal the same
    // mistake would make undo replay a session as the wrong operations — a Move read as a
    // Quarantine, a Rename read as a Delete — turning the safety net into the hazard.
    [Fact]
    public void Json_options_are_constructed_only_by_the_central_serializer()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (Path.GetFileName(file).Equals("ArkJson.cs", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.ReadAllText(file).Contains("new JsonSerializerOptions", StringComparison.Ordinal) ||
                File.ReadAllText(file).Contains("JsonSerializerOptions Options = new", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "JsonSerializerOptions must come from ArkJson so every enum is written by name. Offenders:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Every_enum_round_trips_through_its_name_not_its_ordinal()
    {
        var enums = typeof(ARK.Core.Execution.ActionKind).Assembly
            .GetExportedTypes()
            .Where(type => type.IsEnum)
            .ToArray();

        Assert.NotEmpty(enums);

        var offenders = new List<string>();
        foreach (var type in enums)
        {
            var value = Enum.GetValues(type).GetValue(0)!;
            var json = System.Text.Json.JsonSerializer.Serialize(value, type, ARK.Core.Serialization.ArkJson.Write);

            if (!json.Trim().StartsWith('"'))
            {
                offenders.Add($"{type.Name} serialized as {json}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Enums must serialize as names. Offenders:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // Phase 7 gate 4. RemoveDirectory and DeleteFile exist so undo can restore an exact prior
    // state. "Produced by inversion, never by an operation" was convention; this makes it
    // structural, the same way the Executor rule is. Without it, Prohibition 5 — never delete,
    // quarantine — is one careless call away from being bypassed.
    private static readonly string[] InversionOnlyKinds =
    {
        "ActionKind.RemoveDirectory",
        "ActionKind.DeleteFile",
    };

    private static readonly string[] InversionOnlyFiles =
    {
        "JournalInverter.cs", // constructs them
        "ActionKind.cs",      // declares them
        "Executor.cs",        // performs them
        "UndoService.cs",     // checks their preconditions
    };

    [Fact]
    public void Only_the_inverter_constructs_inversion_only_actions()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources(sourceRoot))
        {
            if (InversionOnlyFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var kind in InversionOnlyKinds)
            {
                if (text.Contains(kind, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} references '{kind}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "RemoveDirectory and DeleteFile are produced by inversion only — an operation that needs to " +
            "remove something quarantines it instead. Offenders:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> EnumerateProductionSources(string sourceRoot)
    {
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";

        return Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(obj, StringComparison.Ordinal) &&
                !path.Contains(bin, StringComparison.Ordinal));
    }

    private static string FindSourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(Path.Combine(src, "ARK.Core")) &&
                Directory.Exists(Path.Combine(src, "ARK.Cli")))
            {
                return src;
            }
        }

        throw new DirectoryNotFoundException("Could not locate src/ containing ARK.Core and ARK.Cli.");
    }
}
