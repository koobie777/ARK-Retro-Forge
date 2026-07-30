using System.Diagnostics.CodeAnalysis;
using ARK.Core.Diagnostics;
using ARK.Core.Instances;
using ARK.Core.Tools;

namespace ARK.Tests;

public sealed class MedicalBayServiceTests : IDisposable
{
    private readonly string _root;
    private readonly InstancePaths _paths;

    public MedicalBayServiceTests()
    {
        _root = TempRoot.Create();
        _paths = new InstancePaths("medbay-tests", _root);
    }

    public void Dispose()
    {
        TempRoot.Delete(_root);
        GC.SuppressFinalize(this);
    }

    // Empty PATH so no real tool is located and no version process is ever spawned.
    private MedicalBayService NewService() =>
        new(_paths, new ToolManager(new ToolLocator(_paths.ToolsRoot, pathVariable: string.Empty)), new DatStatusReporter(_paths));

    // Gate 3 — the service produces a full report with no console (no AnsiConsole, no System.Console).
    [Fact]
    [RequiresUnreferencedCode("Exercises MedicalBayService.Generate.")]
    public void Generate_produces_a_full_report_with_no_console_attached()
    {
        var report = NewService().Generate(MedicalBayContext.Empty);

        Assert.Equal("medbay-tests", report.InstanceName);
        Assert.Null(report.RomRoot);
        Assert.False(report.RomRootSet);
        Assert.Equal("psx", report.ActiveSystemProfile.Code);
        Assert.Equal(7, report.Tools.Count);
        Assert.All(report.Tools, tool => Assert.False(tool.IsFound));
        Assert.NotNull(report.DatCatalogs);
    }

    [Fact]
    [RequiresUnreferencedCode("Exercises MedicalBayService.Generate.")]
    public void Generate_reflects_the_supplied_runtime_context()
    {
        var report = NewService().Generate(new MedicalBayContext(RomRoot: @"X:\roms", SystemCode: "psx"));

        Assert.True(report.RomRootSet);
        Assert.Equal(@"X:\roms", report.RomRoot);
    }

    // Gate 9 — the report exposes per-field status only, no global pass/fail verdict.
    [Fact]
    public void Report_exposes_no_global_pass_fail_verdict()
    {
        var properties = typeof(MedicalBayReport).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "InstanceName", "RomRoot", "RomRootSet", "ActiveSystemProfile", "Tools", "DatCatalogs"
            },
            properties);

        Assert.DoesNotContain(properties, name =>
            name.Contains("Verdict", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Passed", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AllTools", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Required", StringComparison.OrdinalIgnoreCase));
    }
}
