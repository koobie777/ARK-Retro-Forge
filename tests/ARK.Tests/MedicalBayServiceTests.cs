using System.Diagnostics.CodeAnalysis;
using ARK.Core.Dat;
using ARK.Core.Diagnostics;
using ARK.Core.Instances;
using ARK.Core.Systems;
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

    private static SystemRegistry Systems() =>
        SystemRegistry.FromDefinitions([new SystemDefinition { Code = "n64", DisplayName = "Nintendo 64" }]);

    // Empty PATH so no real tool is located and no version process is ever spawned.
    private MedicalBayService NewService() =>
        new(
            _paths,
            new ToolManager(new ToolLocator(_paths.ToolsRoot, pathVariable: string.Empty)),
            new DatCatalog(_paths),
            Systems());

    // Gate 3 — the service produces a full report with no console (no AnsiConsole, no System.Console).
    [Fact]
    [RequiresUnreferencedCode("Exercises MedicalBayService.Generate.")]
    public void Generate_produces_a_full_report_with_no_console_attached()
    {
        var report = NewService().Generate(MedicalBayContext.Empty);

        Assert.Equal("medbay-tests", report.InstanceName);
        Assert.Null(report.RomRoot);
        Assert.False(report.RomRootSet);
        Assert.Null(report.ActiveSystem.Code);
        Assert.False(report.ActiveSystem.Recognized);
        Assert.Equal(7, report.Tools.Count);
        Assert.All(report.Tools, tool => Assert.False(tool.IsFound));
        Assert.NotNull(report.DatCatalogs);
    }

    [Fact]
    [RequiresUnreferencedCode("Exercises MedicalBayService.Generate.")]
    public void Generate_reflects_the_supplied_runtime_context()
    {
        var report = NewService().Generate(new MedicalBayContext(RomRoot: @"X:\roms", SystemCode: "n64"));

        Assert.True(report.RomRootSet);
        Assert.Equal(@"X:\roms", report.RomRoot);
        Assert.True(report.ActiveSystem.Recognized);
        Assert.Equal("Nintendo 64", report.ActiveSystem.DisplayName);
    }

    // Gate 4 — an unrecognized code is reported as such, never substituted for a default.
    [Fact]
    [RequiresUnreferencedCode("Exercises MedicalBayService.Generate.")]
    public void Generate_reports_unknown_system_as_unrecognized_without_fallback()
    {
        var report = NewService().Generate(new MedicalBayContext(RomRoot: null, SystemCode: "psx"));

        Assert.Equal("psx", report.ActiveSystem.Code);
        Assert.False(report.ActiveSystem.Recognized);
        Assert.Null(report.ActiveSystem.DisplayName);
    }

    // Gate 9 — the report exposes per-field status only, no global pass/fail verdict.
    [Fact]
    public void Report_exposes_no_global_pass_fail_verdict()
    {
        var properties = typeof(MedicalBayReport).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "InstanceName", "RomRoot", "RomRootSet", "ActiveSystem", "Tools", "DatCatalogs"
            },
            properties);

        Assert.DoesNotContain(properties, name =>
            name.Contains("Verdict", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Passed", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AllTools", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Required", StringComparison.OrdinalIgnoreCase));
    }
}
