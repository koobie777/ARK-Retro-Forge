using ARK.Core.Configuration;
using ARK.Core.Naming;

namespace ARK.Tests;

/// <summary>
/// Phase 3 gate. The corpus is the specification: 9,363 real names carrying only the round-trip
/// invariant, 50 hand-selected traps, and 15 synthesized corruptions with expected recovery.
/// </summary>
public class NamingCorpusTests
{
    private static readonly TokenVocabulary Vocabulary = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());
    private static readonly NameTokenizer Tokenizer = new(Vocabulary);
    private static readonly NameFormatter Formatter = new(Tokenizer);

    // Gate 4. Decompose, rebuild, decompose again — identical tokens both times. Idempotency as
    // an executable property, which is what makes stacking structurally impossible rather than
    // something we hope was fixed.
    [Fact]
    public void Round_trip_invariant_holds_for_every_real_name()
    {
        var failures = new List<string>();

        foreach (var name in TestFixtures.ReadCorpus("real-names.txt"))
        {
            var first = Tokenizer.Parse(name);
            if (!Formatter.TryFormat(first, out var rebuilt))
            {
                failures.Add($"refused to format: {name}");
                continue;
            }

            var second = Tokenizer.Parse(rebuilt);
            if (!first.Equals(second))
            {
                failures.Add($"{name}{Environment.NewLine}    first  = {first}{Environment.NewLine}    second = {second}");
            }
        }

        Assert.True(failures.Count == 0, Report("parse(format(parse(x))) != parse(x)", failures));
    }

    // Gate 5. Every name in the corpus is already canonical, so a correct tokenizer is a no-op on
    // it. Any divergence is a bug in one direction or the other.
    [Fact]
    public void Canonical_names_are_reproduced_exactly()
    {
        var failures = new List<string>();

        foreach (var name in TestFixtures.ReadCorpus("real-names.txt"))
        {
            var parsed = Tokenizer.Parse(name);
            if (!Formatter.TryFormat(parsed, out var formatted))
            {
                failures.Add($"refused to format: {name}");
            }
            else if (!string.Equals(formatted, name, StringComparison.Ordinal))
            {
                failures.Add($"expected: {name}{Environment.NewLine}    actual: {formatted}");
            }
        }

        Assert.True(failures.Count == 0, Report("format(parse(x)) != x", failures));
    }

    // Gate 12, corpus-wide. The unknown bucket is the input queue for extending the vocabulary
    // tables; if it silently empties, that feedback loop is gone.
    [Fact]
    public void Unknown_bucket_never_silently_empties()
    {
        var withUnknowns = TestFixtures.ReadCorpus("real-names.txt")
            .Select(Tokenizer.Parse)
            .Count(parsed => parsed.UnknownTokens.Count > 0);

        Assert.True(withUnknowns > 0, "No name produced an unknown token — the unknown bucket has silently emptied.");
    }

    // Gate 6. Each row is a rule drawn from real data that the tokenizer must get right.
    [Theory]
    [MemberData(nameof(TrapCases))]
    public void Trap_case_classifies_correctly(string category, string name)
    {
        var parsed = Tokenizer.Parse(name);
        Assert.True(parsed.IsTokenizable, $"{name} was flagged {parsed.Flag}");

        Assert.True(Formatter.TryFormat(parsed, out var formatted));
        Assert.Equal(name, formatted);
        Assert.Equal(parsed, Tokenizer.Parse(formatted));

        switch (category)
        {
            case "boundary":
                // The parenthetical before the region belongs to the title, and stays there.
                Assert.Contains("(", parsed.Title, StringComparison.Ordinal);
                break;
            case "rev-alpha":
                Assert.Equal(RevisionScheme.Alphabetic, parsed.Revision.Scheme);
                break;
            case "rev-double":
                Assert.Equal(RevisionScheme.Numeric, parsed.Revision.Scheme);
                Assert.True(parsed.Revision.Number >= 10, $"expected a double-digit revision, got {parsed.Revision.Number}");
                break;
            case "lang-locale":
                Assert.Contains(parsed.Languages, code => code.Contains('-', StringComparison.Ordinal));
                AssertNoLanguageInRegionSlot(parsed);
                break;
            case "lang-plus":
                // '+' separates distinct language SETS; it is preserved verbatim, never flattened.
                Assert.Contains(parsed.TokensOf(TokenCategory.Language), token => token.Value.Contains('+', StringComparison.Ordinal));
                break;
            case "lang-vs-region":
                AssertNoLanguageInRegionSlot(parsed);
                Assert.NotEmpty(parsed.Languages);
                break;
            case "comma-nonregion":
                // Structurally identical to a region list; only vocabulary separates them.
                Assert.Equal(new[] { "USA" }, parsed.Regions);
                break;
            case "article":
                Assert.Contains(", The", parsed.Title, StringComparison.Ordinal);
                break;
            case "article-in-token":
                Assert.Contains(parsed.Tokens, token => token.Value.EndsWith(", The", StringComparison.Ordinal));
                break;
            case "nongame":
                Assert.NotEmpty(parsed.TokensOf(TokenCategory.NonGame));
                break;
            case "licensing":
                Assert.NotEmpty(parsed.TokensOf(TokenCategory.Licensing));
                break;
            case "hardware":
                Assert.NotEmpty(parsed.TokensOf(TokenCategory.Hardware));
                break;
            case "compilation":
                Assert.NotEmpty(parsed.TokensOf(TokenCategory.Compilation));
                break;
            case "serial":
                Assert.NotEmpty(parsed.TokensOf(TokenCategory.Serial));
                break;
            case "disc":
                Assert.NotEmpty(parsed.TokensOf(TokenCategory.Disc));
                break;
            case "stacked-tokens":
                Assert.True(parsed.Tokens.Count >= 5, $"expected 5+ tokens, got {parsed.Tokens.Count}");
                Assert.Equal(parsed.Tokens.Count, parsed.Tokens.Distinct().Count());
                break;
            default:
                Assert.Fail($"Unhandled trap category '{category}' — the test must assert something for every row.");
                break;
        }
    }

    // Gate 7. A clean set contains zero of these, which is exactly why they were synthesized.
    [Theory]
    [MemberData(nameof(DirtyCases))]
    public void Dirty_case_recovers_to_expected_canonical_output(string kind, string dirty, string expected)
    {
        var parsed = Tokenizer.Parse(dirty);

        if (expected.StartsWith("FLAG:", StringComparison.Ordinal))
        {
            var flag = expected["FLAG:".Length..];
            Assert.False(parsed.IsTokenizable, $"[{kind}] expected a refusal, got a parse");
            Assert.Equal("no-region", flag);
            Assert.Equal(NameParseFlag.NoRegion, parsed.Flag);
            Assert.False(Formatter.TryFormat(parsed, out _), "a flagged name must produce no output at all");
            return;
        }

        Assert.True(Formatter.TryFormat(parsed, out var formatted), $"[{kind}] refused to format");
        Assert.Equal(expected, formatted);

        // Recovery must also be a fixed point: re-running it changes nothing further.
        Assert.Equal(expected, Formatter.Format(Tokenizer.Parse(formatted)));
    }

    // Gate 8. The v1 regression, asserted by name: a positional regex captured region
    // "En,Fr,De" here, corrupting a clean No-Intro filename on first contact.
    [Fact]
    public void Language_tag_never_lands_in_the_region_slot()
    {
        var parsed = Tokenizer.Parse("Tekken 3 (USA) (En,Fr,De)");

        Assert.Equal(new[] { "USA" }, parsed.Regions);
        Assert.Equal(new[] { "En", "Fr", "De" }, parsed.Languages);
        Assert.Equal("Tekken 3", parsed.Title);
        Assert.Equal("Tekken 3 (USA) (En,Fr,De)", Formatter.Format(parsed));
    }

    // Gate 9. v1's title regex swallowed the prior tag and the formatter re-appended it, so each
    // run added another copy.
    [Fact]
    public void Stacked_region_tokens_collapse_to_one()
    {
        var parsed = Tokenizer.Parse("Crash Bandicoot (USA) (USA) (USA)");

        Assert.Single(parsed.TokensOf(TokenCategory.Region));
        Assert.Equal("Crash Bandicoot", parsed.Title);
        Assert.Equal("Crash Bandicoot (USA)", Formatter.Format(parsed));
    }

    // Gate 10. Identical in shape, distinguishable only by side of the region boundary.
    [Fact]
    public void Title_parenthetical_survives_while_metadata_after_the_boundary_does_not()
    {
        const string Name = "Interactive CD Sampler Pack Volume Three (Version 3.5) (USA) (Rev 1)";
        var parsed = Tokenizer.Parse(Name);

        Assert.Equal("Interactive CD Sampler Pack Volume Three (Version 3.5)", parsed.Title);
        Assert.Empty(parsed.TokensOf(TokenCategory.Version));
        Assert.Equal(RevisionValue.Numeric(1), parsed.Revision);
        Assert.Equal(Name, Formatter.Format(parsed));
    }

    // Gate 11.
    [Fact]
    public void Name_without_a_recognized_region_is_flagged_not_guessed()
    {
        var parsed = Tokenizer.Parse("Adventure Time - Explore the Dungeon Because I Don't Know!");

        Assert.False(parsed.IsTokenizable);
        Assert.Equal(NameParseFlag.NoRegion, parsed.Flag);
        Assert.Empty(parsed.Tokens);
        Assert.False(Formatter.TryFormat(parsed, out _));
        Assert.Throws<InvalidOperationException>(() => Formatter.Format(parsed));
    }

    // Gate 12.
    [Fact]
    public void Unknown_token_is_preserved_and_retrievable()
    {
        var parsed = Tokenizer.Parse("Adventure Time (USA) (Wobbleflex)");

        var unknown = Assert.Single(parsed.UnknownTokens);
        Assert.Equal("Wobbleflex", unknown.Value);
        Assert.Equal("Adventure Time (USA) (Wobbleflex)", Formatter.Format(parsed));
    }

    // Gate 13.
    [Fact]
    public void Revision_schemes_parse_and_never_compare_across_schemes()
    {
        Assert.Equal(RevisionValue.Alphabetic('A'), Tokenizer.Parse("Action 52 (USA) (Rev A) (Unl)").Revision);
        Assert.Equal(RevisionValue.Numeric(10), Tokenizer.Parse("Game (USA) (Rev 10)").Revision);

        // Rev 10 sorts above Rev 9 — a string sort places it between Rev 1 and Rev 2.
        Assert.True(RevisionValue.Numeric(10).TryCompareTo(RevisionValue.Numeric(9), out var numeric));
        Assert.True(numeric > 0);

        // Numeric against alphabetic is refused rather than answered wrongly.
        Assert.False(RevisionValue.Numeric(1).TryCompareTo(RevisionValue.Alphabetic('B'), out _));
        Assert.False(RevisionValue.Alphabetic('B').TryCompareTo(RevisionValue.Numeric(1), out _));

        // An absent tag is Rev 0 — the original, and the OLDEST.
        Assert.True(RevisionValue.Absent.TryCompareTo(RevisionValue.Numeric(1), out var absent));
        Assert.True(absent < 0);
        Assert.Equal(RevisionValue.Absent, Tokenizer.Parse("Game (USA)").Revision);
    }

    // Article inversion groups both spellings onto one key without rewriting either token.
    [Fact]
    public void Article_inversion_groups_both_forms_onto_one_key()
    {
        Assert.Equal(
            Vocabulary.NormalizeForGrouping("Legend of Zelda, The"),
            Vocabulary.NormalizeForGrouping("The Legend of Zelda"));

        Assert.Equal(
            Vocabulary.NormalizeForGrouping("Cowabunga Collection, The"),
            Vocabulary.NormalizeForGrouping("The Cowabunga Collection"));
    }

    // Real names whose leading word is an article in SOME language, which is why only the
    // unambiguous English definite article is ever rewritten.
    [Theory]
    [InlineData("Die Hard (USA)")]
    [InlineData("Die Hard Trilogy 2 - Viva Las Vegas (USA)")]
    [InlineData("Las Vegas Cool Hand (USA) (GB Compatible)")]
    [InlineData("An M. Night Shyamalan Film - The Last Airbender (USA) (En,Fr) (NDSi Enhanced)")]
    public void Ambiguous_leading_article_is_never_rewritten(string name) =>
        Assert.Equal(name, Formatter.Format(Tokenizer.Parse(name)));

    // A bracket flag BEFORE the region boundary is title content — the boundary rule handles it
    // with no special case for brackets.
    [Fact]
    public void Bracket_before_the_boundary_stays_in_the_title()
    {
        var parsed = Tokenizer.Parse("[BIOS] Challenger GB (USA) (Unl)");

        Assert.Equal("[BIOS] Challenger GB", parsed.Title);
        Assert.Empty(parsed.BracketFlags);
        Assert.Equal("[BIOS] Challenger GB (USA) (Unl)", Formatter.Format(parsed));
    }

    public static TheoryData<string, string> TrapCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var line in TestFixtures.ReadCorpus("traps.tsv"))
        {
            var fields = line.Split('\t');
            data.Add(fields[0], fields[3]);
        }

        return data;
    }

    public static TheoryData<string, string, string> DirtyCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var line in TestFixtures.ReadCorpus("dirty.tsv"))
        {
            var fields = line.Split('\t');
            data.Add(fields[0], fields[1], fields[2]);
        }

        return data;
    }

    private static void AssertNoLanguageInRegionSlot(ParsedName parsed)
    {
        foreach (var region in parsed.Regions)
        {
            Assert.DoesNotContain(parsed.Languages, code => string.Equals(code, region, StringComparison.Ordinal));
        }
    }

    private static string Report(string headline, IReadOnlyList<string> failures) =>
        $"{failures.Count} name(s) where {headline}:" +
        Environment.NewLine +
        string.Join(Environment.NewLine, failures.Take(25).Select(failure => "  " + failure));
}
