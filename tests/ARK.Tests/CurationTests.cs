using ARK.Core.Configuration;
using ARK.Core.Naming;
using ARK.Core.Policy;
using ARK.Core.Scanning;
using ARK.Core.Settings;
using ARK.Core.Units;
using ARK.Core.Verification;

namespace ARK.Tests;

/// <summary>
/// Phase 8: variants are distinct releases, and curating them is a preference, not a redundancy
/// claim.
/// </summary>
/// <remarks>
/// Dedup could argue a removed file was recoverable from its byte-identical twin. Nothing here
/// can — every removal is a real loss of a real release. That sets the standard for what the
/// engine is allowed to decide on its own, and for how loudly it refuses when it cannot.
/// </remarks>
public class CurationTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);

    // Gate 3. The likeliest bug in this phase: untagged is the ORIGINAL, so it is the oldest.
    [Fact]
    public void Untagged_release_is_rev_zero_and_therefore_the_oldest()
    {
        var report = Curate(VariantPresets.LatestRevision, "Tekken 3 (USA)", "Tekken 3 (USA) (Rev 1)");

        var group = Assert.Single(report.Multi);
        var kept = Assert.Single(group.Keep);

        Assert.Equal("Tekken 3 (USA) (Rev 1).zip", kept.FileName);
        Assert.Equal("Tekken 3 (USA).zip", Assert.Single(group.Remove).FileName);
    }

    // Gate 4. String sort puts Rev 10 between Rev 1 and Rev 2, and v1.10 below v1.9.
    [Fact]
    public void Rev_10_ranks_above_rev_9()
    {
        var report = Curate(VariantPresets.LatestRevision, "Game (USA) (Rev 9)", "Game (USA) (Rev 10)");

        Assert.Equal("Game (USA) (Rev 10).zip", Assert.Single(Assert.Single(report.Multi).Keep).FileName);
    }

    [Fact]
    public void Version_1_10_ranks_above_1_9()
    {
        Assert.True(AxisOrdering.CompareSegments("1.10", "1.9") > 0);
        Assert.True(AxisOrdering.CompareSegments("v1.10", "v1.9") > 0);
        Assert.True(AxisOrdering.CompareSegments("2.0", "1.99") > 0);
    }

    // Gate 5. Rev A and Rev 1 are separate schemes; neither is a position in the other's sequence.
    [Fact]
    public void Numeric_and_alphabetic_revisions_in_one_group_are_flagged_not_ordered()
    {
        var report = Curate(VariantPresets.LatestRevision, "Action 52 (USA) (Rev 1)", "Action 52 (USA) (Rev A)");

        var group = Assert.Single(report.Multi);

        Assert.True(group.IsRefused);
        Assert.Empty(group.Remove);
        Assert.Equal(2, group.Keep.Count);
        Assert.Contains(group.Refusals, refusal => refusal.Axis == VariantAxis.Revision);
        Assert.Contains("do not compare", Assert.Single(group.Refusals).Reason, StringComparison.OrdinalIgnoreCase);
    }

    // Gate 6. Unambiguous and safe to enforce.
    [Theory]
    [InlineData("Alpha")]
    [InlineData("Beta")]
    [InlineData("Proto")]
    [InlineData("Sample")]
    [InlineData("Demo")]
    public void Retail_outranks_every_pre_release_build(string status)
    {
        var report = Curate(VariantPresets.RetailOnly, "Game (USA)", $"Game (USA) ({status})");

        var group = Assert.Single(report.Multi);
        Assert.Equal("Game (USA).zip", Assert.Single(group.Keep).FileName);
        Assert.Equal($"Game (USA) ({status}).zip", Assert.Single(group.Remove).FileName);
    }

    // Gate 7. Alpha/Beta/Proto have no consistent cross-publisher timeline.
    [Fact]
    public void Two_pre_release_builds_with_no_date_are_refused_not_ordered()
    {
        var report = Curate(VariantPresets.Everything, "Game (USA) (Beta)", "Game (USA) (Proto)");

        var group = Assert.Single(report.Multi);

        Assert.True(group.IsRefused);
        Assert.Empty(group.Remove);
        Assert.Contains(group.Refusals, refusal => refusal.Axis == VariantAxis.DevStatus);
        Assert.Contains("no reliable order", Assert.Single(group.Refusals).Reason, StringComparison.OrdinalIgnoreCase);
    }

    // A date tag is the one signal that does separate them.
    [Fact]
    public void Pre_release_builds_with_date_tags_are_ordered_by_date()
    {
        var left = Tokenizer.Parse("Game (USA) (Proto) (1993-10-11)");
        var right = Tokenizer.Parse("Game (USA) (Beta) (1994-08-09)");

        Assert.Equal(AxisComparison.RightWins, AxisOrdering.Compare(VariantAxis.DevStatus, left, right, out _));
    }

    // Gate 8.
    [Fact]
    public void A_group_the_policy_cannot_order_is_reported_and_skipped()
    {
        var report = Curate(VariantPresets.Everything, "Game (USA) (Beta)", "Game (USA) (Proto)");

        Assert.Single(report.Refused);
        Assert.Empty(report.Resolved.SelectMany(group => group.Remove));
        Assert.Empty(report.Removable);
    }

    // Gate 9. Development status covers 245 files on the reference corpus and licensing covers
    // 390 — different files, different intent. Wanting no prototypes says nothing about homebrew.
    [Fact]
    public void Keep_retail_only_leaves_unlicensed_titles_untouched()
    {
        var report = Curate(VariantPresets.RetailOnly, "Action 52 (USA) (Unl)", "Action 52 (USA) (Proto) (Unl)");

        var group = Assert.Single(report.Multi);

        // The proto goes because it is pre-release, not because it is unlicensed.
        Assert.Equal("Action 52 (USA) (Unl).zip", Assert.Single(group.Keep).FileName);
        Assert.Equal(AxisRole.Identity, VariantPresets.RetailOnly.RuleFor(VariantAxis.Licensing).Role);
    }

    [Fact]
    public void An_unlicensed_only_group_is_left_entirely_alone_by_retail_only()
    {
        var report = Curate(VariantPresets.RetailOnly, "Homebrew Game (USA) (Unl)", "Homebrew Game (USA) (Aftermarket)");

        Assert.Empty(report.Removable);
    }

    // Gate 10. Hardware describes cartridge capability, not release lineage.
    [Fact]
    public void Hardware_flags_never_affect_grouping_or_ranking()
    {
        var plain = Tokenizer.Parse("Aladdin (USA)");
        var enhanced = Tokenizer.Parse("Aladdin (USA) (SGB Enhanced)");

        // Same grouping key despite the extra token.
        Assert.Equal(
            VariantGrouping.KeyFor(plain, Vocabulary, VariantPresets.ReportOnly),
            VariantGrouping.KeyFor(enhanced, Vocabulary, VariantPresets.ReportOnly));

        Assert.False(VariantGrouping.IsIdentity(TokenCategory.Hardware, VariantPresets.ReportOnly));

        // And no axis maps to it, so it can never be ranked on.
        Assert.Null(VariantAxes.For(TokenCategory.Hardware));
    }

    // Gate 11. A Bonus Disc is not a revision of the game it shipped beside.
    [Theory]
    [InlineData("Mario Kart - Double Dash!! (USA) (Bonus Disc)")]
    [InlineData("Some Game (USA) (DLC)")]
    [InlineData("Some Game (USA) (Save Data)")]
    public void Non_game_content_is_excluded_from_variant_grouping(string name)
    {
        var report = Curate(VariantPresets.Everything, "Some Game (USA)", name);

        Assert.Single(report.ExcludedFor(CurationExclusion.NonGameContent));
        Assert.Empty(report.Multi);
    }

    // Gate 12. Canonical token order is partial, so two equivalent names can format differently.
    [Fact]
    public void Grouping_uses_token_sets_not_formatted_strings()
    {
        var a = Tokenizer.Parse("GameShark CDX (USA) (Unl) (v3.4)");
        var b = Tokenizer.Parse("GameShark CDX (USA) (v3.4) (Unl)");

        Assert.NotEqual(a.ToString(), b.ToString());
        Assert.Equal(
            VariantGrouping.KeyFor(a, Vocabulary, VariantPresets.ReportOnly),
            VariantGrouping.KeyFor(b, Vocabulary, VariantPresets.ReportOnly));
    }

    // Article inversion is a spelling difference, not a different game.
    [Fact]
    public void Article_inversion_does_not_split_a_group()
    {
        Assert.Equal(
            VariantGrouping.KeyFor(Tokenizer.Parse("The Alliance Alive (USA)"), Vocabulary, VariantPresets.ReportOnly),
            VariantGrouping.KeyFor(Tokenizer.Parse("Alliance Alive, The (USA)"), Vocabulary, VariantPresets.ReportOnly));
    }

    // Gate 13. The same collection curates differently depending on this, so it must be stated.
    [Fact]
    public void The_report_states_which_axes_are_identity_versus_variance()
    {
        var report = Curate(VariantPresets.RetailOnly, "Game (USA)", "Game (USA) (Proto)");

        Assert.Contains(VariantAxis.DevStatus, report.VarianceAxes);
        Assert.Contains(VariantAxis.Region, report.IdentityAxes);
        Assert.DoesNotContain(VariantAxis.DevStatus, report.IdentityAxes);
    }

    // Region as identity means different regions are different games; as variance they compete.
    [Fact]
    public void Region_as_identity_versus_variance_changes_the_grouping()
    {
        var usa = Tokenizer.Parse("Game (USA)");
        var japan = Tokenizer.Parse("Game (Japan)");

        var identity = new VariantPolicy("region-identity", new[] { new AxisRule(VariantAxis.Region, AxisRole.Identity) });
        var variance = new VariantPolicy("region-variance", new[] { new AxisRule(VariantAxis.Region, AxisRole.Variance) });

        Assert.NotEqual(
            VariantGrouping.KeyFor(usa, Vocabulary, identity),
            VariantGrouping.KeyFor(japan, Vocabulary, identity));

        Assert.Equal(
            VariantGrouping.KeyFor(usa, Vocabulary, variance),
            VariantGrouping.KeyFor(japan, Vocabulary, variance));
    }

    // Gate 14. Every preset is just a set of per-axis rules.
    [Fact]
    public void Presets_are_expressed_as_per_axis_rules_with_none_special_cased()
    {
        foreach (var preset in VariantPresets.All.Values)
        {
            Assert.NotEmpty(preset.Rules);
            Assert.All(preset.Rules, rule => Assert.True(Enum.IsDefined(rule.Axis)));
        }

        // Report-only removes nothing because every axis is identity — not because of a check.
        Assert.True(VariantPresets.ReportOnly.IsReportOnly);
        Assert.All(VariantPresets.ReportOnly.Rules, rule => Assert.Equal(AxisRole.Identity, rule.Role));

        // A hand-built policy identical to a preset behaves identically.
        var handBuilt = new VariantPolicy("mine", VariantPresets.RetailOnly.Rules);
        var fromPreset = Curate(VariantPresets.RetailOnly, "Game (USA)", "Game (USA) (Proto)");
        var fromCustom = Curate(handBuilt, "Game (USA)", "Game (USA) (Proto)");

        Assert.Equal(fromPreset.Removable.Count, fromCustom.Removable.Count);
    }

    // Gate 15.
    [Fact]
    public void Policies_round_trip_through_settings()
    {
        var original = VariantPresets.OneGameOneRom;

        var restored = PolicySettings.ToPolicy(PolicySettings.ToSettings(original));

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Regions, restored.Regions);
        foreach (var axis in Enum.GetValues<VariantAxis>())
        {
            Assert.Equal(original.RuleFor(axis).Role, restored.RuleFor(axis).Role);
            Assert.Equal(original.RuleFor(axis).Selection, restored.RuleFor(axis).Selection);
            Assert.Equal(original.RuleFor(axis).Keep, restored.RuleFor(axis).Keep);
        }
    }

    [Fact]
    public void A_named_preset_resolves_from_settings_and_an_unknown_name_falls_back_to_report_only()
    {
        Assert.Equal(
            VariantPresets.RetailOnly.Name,
            PolicySettings.Resolve(new ArkSettings { CurationPolicy = "retail-only" }).Name);

        // A typo must never quietly curate a collection.
        Assert.True(PolicySettings.Resolve(new ArkSettings { CurationPolicy = "retial-only" }).IsReportOnly);
        Assert.True(PolicySettings.Resolve(new ArkSettings()).IsReportOnly);
    }

    [Fact]
    public void A_custom_policy_resolves_from_settings()
    {
        var settings = new ArkSettings
        {
            CurationPolicy = "mine",
            CustomCurationPolicy = PolicySettings.ToSettings(
                new VariantPolicy("mine", new[]
                {
                    new AxisRule(VariantAxis.Revision, AxisRole.Variance, AxisSelection.KeepBest),
                })),
        };

        var resolved = PolicySettings.Resolve(settings);

        Assert.Equal("mine", resolved.Name);
        Assert.Equal(AxisSelection.KeepBest, resolved.RuleFor(VariantAxis.Revision).Selection);
    }

    // Gate 16.
    [Fact]
    public void Report_only_is_the_default_and_removes_nothing()
    {
        var report = Curate(VariantPresets.ReportOnly, "Game (USA)", "Game (USA) (Rev 1)", "Game (USA) (Proto)");

        Assert.True(report.Policy.IsReportOnly);
        Assert.Empty(report.Removable);
        Assert.All(report.Groups, group => Assert.Empty(group.Remove));
    }

    // In Progress and Mismatched never curate, exactly as they never dedup.
    [Theory]
    [InlineData(VerificationState.InProgress, CurationExclusion.InProgress)]
    [InlineData(VerificationState.Mismatched, CurationExclusion.Mismatched)]
    public void Units_that_cannot_be_judged_are_excluded(VerificationState state, CurationExclusion expected)
    {
        var report = Curate(VariantPresets.RetailOnly, state, "Game (USA)", "Game (USA) (Proto)");

        Assert.Empty(report.Removable);
        Assert.Equal(2, report.ExcludedFor(expected).Count);
    }

    // An unparseable name cannot be placed on any axis, so it is set aside rather than guessed at.
    [Fact]
    public void An_unparseable_name_is_excluded_with_a_reason()
    {
        var report = Curate(VariantPresets.RetailOnly, "Game (USA)", "no region here");

        Assert.Single(report.ExcludedFor(CurationExclusion.Unparseable));
    }

    // 1G1R composes four axes at once and still refuses what it cannot rank.
    [Fact]
    public void One_game_one_rom_keeps_the_preferred_region_latest_revision_and_retail()
    {
        var report = Curate(
            VariantPresets.OneGameOneRom,
            "Game (USA)",
            "Game (USA) (Rev 1)",
            "Game (Japan) (Rev 1)",
            "Game (USA) (Rev 1) (Proto)");

        var group = Assert.Single(report.Multi);
        var kept = Assert.Single(group.Keep);

        Assert.Equal("Game (USA) (Rev 1).zip", kept.FileName);
        Assert.Equal(3, group.Remove.Count);
    }

    private CurationReport Curate(VariantPolicy policy, params string[] names) =>
        Curate(policy, VerificationState.Verified, names);

    private CurationReport Curate(VariantPolicy policy, VerificationState state, params string[] names)
    {
        const string Directory = @"D:\Set";

        var files = names
            .Select(name => new FileEntry(
                Path.Combine(Directory, name + ".zip"), name + ".zip", ".zip", 1024, DateTimeOffset.UnixEpoch))
            .ToArray();

        var profile = new DirectoryProfile(
            Directory, "Set", files.Length, ".zip", 1, 1, DirectoryOutcome.RomSet, ExclusionReason.None,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<FileEntry>());

        var units = files.Select(file => new ScannedUnit(
            new GameUnit(
                GameUnitKind.Cartridge,
                file.FullPath,
                new[] { file },
                Tokenizer.Parse(file.NameWithoutExtension),
                "Set",
                Array.Empty<string>(),
                Array.Empty<ArchiveEntry>(),
                Array.Empty<GameUnitIssue>()),
            null)).ToArray();

        var scan = new ScanReport(
            Directory,
            new[] { new ScannedDirectory(profile, null) },
            units,
            Array.Empty<ExcludedFile>(),
            Array.Empty<FileEntry>());

        var verification = new VerificationReport(
            Directory,
            units.Select(unit => new VerifiedUnit(
                unit.Unit.PrimaryPath, unit.Unit.Files[0].Name, "Set", null, state, "test fixture")).ToArray(),
            0, 0, TimeSpan.Zero, 0);

        return new CurationService(Tokenizer, Vocabulary).Analyze(scan, verification, policy);
    }
}
