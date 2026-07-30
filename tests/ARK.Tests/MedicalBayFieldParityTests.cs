using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Cli.Rendering;
using ARK.Core.Diagnostics;
using ARK.Core.Systems;
using ARK.Core.Tools;
using Spectre.Console;

namespace ARK.Tests;

/// <summary>
/// Gate 4: the human render and the --json output are two serializations of one report; neither may
/// carry a field the other cannot see. This is the direct guard against v1's bug, where --json
/// serialized only the tool array.
/// </summary>
public class MedicalBayFieldParityTests
{
    [Fact]
    [RequiresUnreferencedCode("Serializes MedicalBayReport.")]
    public void Json_keys_report_properties_and_rendered_fields_are_the_same_set()
    {
        var report = SampleReport();
        var reportProperties = PropertyNames(typeof(MedicalBayReport));

        using var document = JsonDocument.Parse(MedicalBayJson.Serialize(report));
        var jsonKeys = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        var renderedFields = MedicalBayRenderer.CoveredReportFields.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(reportProperties, jsonKeys);        // --json shows every field, and nothing extra
        Assert.Equal(reportProperties, renderedFields);  // the human render covers exactly the same set
    }

    [Fact]
    [RequiresUnreferencedCode("Serializes MedicalBayReport.")]
    public void Json_exposes_every_field_of_the_nested_report_types()
    {
        var report = SampleReport();
        using var document = JsonDocument.Parse(MedicalBayJson.Serialize(report));
        var root = document.RootElement;

        AssertObjectKeys(root.GetProperty("ActiveSystemProfile"), typeof(SystemProfile));
        AssertObjectKeys(root.GetProperty("Tools")[0], typeof(ToolCheckResult));
        AssertObjectKeys(root.GetProperty("DatCatalogs")[0], typeof(DatStatus));
    }

    [Fact]
    public void Human_render_surfaces_every_report_value()
    {
        var report = SampleReport();
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 500;

        MedicalBayRenderer.Render(console, report);
        var text = writer.ToString();

        Assert.Contains("inst-parity", text, StringComparison.Ordinal);          // InstanceName
        Assert.Contains(@"R:\roms", text, StringComparison.Ordinal);             // RomRoot + RomRootSet
        Assert.Contains("Sony PlayStation", text, StringComparison.Ordinal);     // ActiveSystemProfile
        Assert.Contains("chdman", text, StringComparison.Ordinal);              // Tools
        Assert.Contains("0.261", text, StringComparison.Ordinal);              // tool version/minimum
        Assert.Contains("n64", text, StringComparison.Ordinal);                // DatCatalogs system
    }

    private static MedicalBayReport SampleReport() => new()
    {
        InstanceName = "inst-parity",
        RomRoot = @"R:\roms",
        RomRootSet = true,
        ActiveSystemProfile = new SystemProfile("psx", "Sony PlayStation", "PS1 orchestration"),
        Tools =
        [
            new ToolCheckResult
            {
                Name = "chdman",
                IsFound = true,
                Path = @"C:\tools\chdman.exe",
                Version = "0.261",
                MinimumVersion = "0.261",
                MeetsMinimumVersion = true,
                ErrorMessage = null
            }
        ],
        DatCatalogs =
        [
            new DatStatus { System = "n64", LocalFileCount = 3, LastUpdatedUtc = DateTime.UtcNow }
        ]
    };

    private static string[] PropertyNames(Type type) =>
        type.GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static void AssertObjectKeys(JsonElement element, Type type)
    {
        var jsonKeys = element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(PropertyNames(type), jsonKeys);
    }
}
