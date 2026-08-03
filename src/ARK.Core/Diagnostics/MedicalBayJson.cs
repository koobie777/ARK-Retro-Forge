using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Serialization;

namespace ARK.Core.Diagnostics;

/// <summary>
/// The single serialization of <see cref="MedicalBayReport"/> to JSON. Both the CLI <c>--json</c>
/// output and the field-parity test go through here, so they can never diverge.
/// </summary>
public static class MedicalBayJson
{
    /// <summary>Serializes the whole report to indented JSON.</summary>
    [RequiresUnreferencedCode("Serializes MedicalBayReport with reflection-based System.Text.Json.")]
    public static string Serialize(MedicalBayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, ArkJson.Write);
    }
}
