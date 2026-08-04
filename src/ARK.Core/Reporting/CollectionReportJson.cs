using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Serialization;

namespace ARK.Core.Reporting;

/// <summary>
/// The single serialization of a <see cref="CollectionReport"/> to JSON. The CLI's <c>--json</c>
/// output and the field-parity test both go through here, so they cannot diverge.
/// </summary>
public static class CollectionReportJson
{
    /// <summary>Serializes the whole report to indented JSON.</summary>
    [RequiresUnreferencedCode("Serializes CollectionReport with reflection-based System.Text.Json.")]
    public static string Serialize(CollectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, ArkJson.Write);
    }
}
