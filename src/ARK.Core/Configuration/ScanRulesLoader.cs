using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Scanning;

namespace ARK.Core.Configuration;

/// <summary>
/// Materializes <see cref="ScanRules"/> from <c>config/scan/scan-rules.json</c>. A missing or
/// unreadable file yields the shipped defaults rather than an error — a scan is read-only, so
/// running one must never be blocked by a config problem.
/// </summary>
public static class ScanRulesLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Loads the rules, falling back to defaults when the file is absent or malformed.</summary>
    [RequiresUnreferencedCode("Deserializes ScanRules with reflection-based System.Text.Json.")]
    public static ScanRules Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ScanRules();
        }

        try
        {
            return JsonSerializer.Deserialize<ScanRules>(File.ReadAllText(path), JsonOptions) ?? new ScanRules();
        }
        catch (JsonException)
        {
            return new ScanRules();
        }
    }
}
