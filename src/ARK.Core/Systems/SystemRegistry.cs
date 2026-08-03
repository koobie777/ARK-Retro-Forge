using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Serialization;

namespace ARK.Core.Systems;

/// <summary>
/// The set of system definitions loaded from <c>config/systems/*.json</c>. Unknown codes resolve to
/// <c>null</c> — never substituted, never defaulted. (v1's registry silently fell back to <c>psx</c>,
/// so a wrong answer was delivered confidently; that is the defect this type fixes.)
/// </summary>
public sealed class SystemRegistry
{
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
    /// Resolves a DAT header name to a system and, where present, a declared format qualifier.
    /// </summary>
    /// <remarks>
    /// <para>Strictly ordered, and deliberately conservative:</para>
    /// <list type="number">
    ///   <item><description>Exact alias match — the system, no qualifier.</description></item>
    ///   <item><description>
    ///     Strip a single trailing parenthetical and match the remainder. The base must match an
    ///     alias <b>and</b> the parenthetical must appear in that system's declared
    ///     <see cref="SystemDefinition.FormatQualifiers"/>.
    ///   </description></item>
    ///   <item><description>Anything else — null. Unrecognized, never guessed.</description></item>
    /// </list>
    /// <para>
    /// The negative cases carry as much weight as the positive ones.
    /// <c>Nintendo - Nintendo 64 (Mario no Photopi SmartMedia)</c> has a matching base but an
    /// undeclared parenthetical: it names a subset, not a format, and treating it as a byte-order
    /// variant would file a handful of SmartMedia dumps as if they were the N64 library.
    /// <c>Nintendo - Nintendo 64DD</c> is a different system whose base never matches at all.
    /// </para>
    /// </remarks>
    public SystemMatch? ResolveDatName(string? datName)
    {
        if (string.IsNullOrWhiteSpace(datName))
        {
            return null;
        }

        var name = datName.Trim();

        var exact = ResolveByAlias(name);
        if (exact is not null)
        {
            return new SystemMatch(exact, null);
        }

        var close = name.LastIndexOf(')');
        if (close != name.Length - 1)
        {
            return null;
        }

        var open = name.LastIndexOf('(');
        if (open <= 0)
        {
            return null;
        }

        var qualifier = name[(open + 1)..close].Trim();
        var baseName = name[..open].Trim();
        if (qualifier.Length == 0 || baseName.Length == 0)
        {
            return null;
        }

        var system = ResolveByAlias(baseName);
        if (system is null)
        {
            return null;
        }

        // The base matched, but an undeclared parenthetical is not a format qualifier. Stop here
        // rather than inventing one — a wrong variant silently merges incompatible hash sets.
        var declared = system.FormatQualifiers
            .FirstOrDefault(candidate => candidate.Equals(qualifier, StringComparison.OrdinalIgnoreCase));

        return declared is null ? null : new SystemMatch(system, declared);
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
                var definition = JsonSerializer.Deserialize<SystemDefinition>(File.ReadAllText(file), ArkJson.Read);
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
