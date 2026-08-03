using ARK.Core.Settings;

namespace ARK.Core.Policy;

/// <summary>
/// Converts policies to and from persisted settings.
/// </summary>
/// <remarks>
/// A custom policy round-trips through exactly the same per-axis rules the shipped presets are
/// built from, so a user can start from a preset, adjust one axis, and save the result without
/// anything special-casing which of the two it is.
/// </remarks>
public static class PolicySettings
{
    /// <summary>
    /// Resolves the policy named in settings.
    /// </summary>
    /// <remarks>
    /// An unrecognized name resolves to report-only, never to a policy that removes files. Getting
    /// this wrong in the other direction would mean a typo in a config file quietly curating a
    /// collection.
    /// </remarks>
    public static VariantPolicy Resolve(ArkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.CustomCurationPolicy is { } custom &&
            string.Equals(custom.Name, settings.CurationPolicy, StringComparison.OrdinalIgnoreCase))
        {
            return ToPolicy(custom);
        }

        return VariantPresets.Find(settings.CurationPolicy) ?? VariantPresets.ReportOnly;
    }

    /// <summary>Converts a persisted custom policy into a usable one.</summary>
    public static VariantPolicy ToPolicy(CustomPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var rules = settings.Rules
            .Select(rule => new AxisRule(
                Parse<VariantAxis>(rule.Axis),
                Parse<AxisRole>(rule.Role),
                Parse<AxisSelection>(rule.Selection),
                rule.Values))
            .ToArray();

        return new VariantPolicy(settings.Name, rules, settings.PreferredRegions);
    }

    /// <summary>Converts a policy into its persisted form.</summary>
    public static CustomPolicySettings ToSettings(VariantPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new CustomPolicySettings(
            policy.Name,
            policy.Rules
                .Select(rule => new AxisRuleSettings(
                    rule.Axis.ToString(),
                    rule.Role.ToString(),
                    rule.Selection.ToString(),
                    rule.Keep.Count == 0 ? null : rule.Keep))
                .ToArray(),
            policy.Regions.Count == 0 ? null : policy.Regions);
    }

    // Unrecognized names take the safe end of each enum: an axis that identifies rather than
    // varies, and a selection that keeps everything.
    private static TEnum Parse<TEnum>(string? value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : default;
}
