using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ARK.Core.Systems;

/// <summary>
/// The set of system definitions loaded from <c>config/systems/*.json</c>. Unknown codes resolve to
/// <c>null</c> — never substituted, never defaulted. (v1's registry silently fell back to <c>psx</c>,
/// so a wrong answer was delivered confidently; that is the defect this type fixes.)
/// </summary>
public sealed class SystemRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IReadOnlyDictionary<string, SystemDefinition> _byCode;
    private readonly IReadOnlyList<SystemDefinition> _all;

    private SystemRegistry(IReadOnlyList<SystemDefinition> definitions)
    {
        _all = definitions;
        _byCode = definitions.ToDictionary(definition => definition.Code, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>All defined systems, ordered by code.</summary>
    public IReadOnlyList<SystemDefinition> All => _all;

    /// <summary>Builds a registry directly from definitions (for programmatic use and tests).</summary>
    public static SystemRegistry FromDefinitions(IEnumerable<SystemDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return new SystemRegistry(definitions
            .OrderBy(definition => definition.Code, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>Resolves a system by its code. Returns null for an unrecognized code — no fallback.</summary>
    public SystemDefinition? Resolve(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return _byCode.TryGetValue(code.Trim(), out var definition) ? definition : null;
    }

    /// <summary>Resolves a system by code or any alias (case-insensitive). Returns null when unmatched.</summary>
    public SystemDefinition? ResolveByAlias(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var direct = Resolve(trimmed);
        if (direct is not null)
        {
            return direct;
        }

        return _all.FirstOrDefault(definition =>
            definition.Aliases.Any(alias => alias.Equals(trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Loads every <c>*.json</c> in <paramref name="systemsDirectory"/>. A missing directory yields
    /// an empty registry. Files are sorted by code so ordering is deterministic.
    /// </summary>
    [RequiresUnreferencedCode("Deserializes SystemDefinition with reflection-based System.Text.Json.")]
    public static SystemRegistry Load(string systemsDirectory)
    {
        var definitions = new List<SystemDefinition>();
        if (Directory.Exists(systemsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(systemsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var definition = JsonSerializer.Deserialize<SystemDefinition>(File.ReadAllText(file), JsonOptions);
                if (definition is not null)
                {
                    definitions.Add(definition);
                }
            }
        }

        return new SystemRegistry(definitions
            .OrderBy(definition => definition.Code, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }
}
