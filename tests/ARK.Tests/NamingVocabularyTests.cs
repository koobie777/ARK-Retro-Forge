using ARK.Core.Configuration;
using ARK.Core.Naming;

namespace ARK.Tests;

/// <summary>
/// Phase 3 gate 3: the vocabularies are data, not code. A term added to
/// <c>config/naming/*.json</c> changes classification with no recompile — which is what makes
/// the unknown bucket an actionable feedback loop rather than a list of complaints.
/// </summary>
public class NamingVocabularyTests
{
    [Fact]
    public void Term_added_by_config_alone_is_recognized_with_no_code_change()
    {
        var shipped = NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory());

        // Unrecognized against the shipped tables: it lands in the unknown bucket, preserved.
        Assert.Equal(TokenCategory.Unknown, shipped.Classify("Wobbleflex").Category);

        var directory = TempRoot.Create();
        try
        {
            foreach (var file in Directory.EnumerateFiles(TestFixtures.ShippedNamingDirectory(), "*.json"))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            }

            File.WriteAllText(
                Path.Combine(directory, "zz-local.json"),
                """{ "category": "Licensing", "terms": ["Wobbleflex"] }""");

            var extended = NamingVocabularyLoader.Load(directory);

            Assert.Equal(TokenCategory.Licensing, extended.Classify("Wobbleflex").Category);

            // The shipped terms still load alongside the addition.
            Assert.Equal(TokenCategory.Licensing, extended.Classify("Unl").Category);
            Assert.Equal(TokenCategory.Region, extended.Classify("USA").Category);
        }
        finally
        {
            TempRoot.Delete(directory);
        }
    }

    [Fact]
    public void Missing_naming_directory_yields_an_empty_vocabulary_rather_than_throwing()
    {
        var vocabulary = NamingVocabularyLoader.Load(Path.Combine(TempRoot.Create(), "absent"));

        Assert.Equal(TokenCategory.Unknown, vocabulary.Classify("USA").Category);
    }

    // Vocabulary decides, position only breaks ties. These four are the cases where any
    // position- or separator-based rule gives the wrong answer.
    [Theory]
    [InlineData("Japan", TokenCategory.Region)]
    [InlineData("Ja", TokenCategory.Language)]
    [InlineData("No", TokenCategory.Language)]
    [InlineData("USA, Europe", TokenCategory.Region)]
    [InlineData("Virtual Console, Switch Online", TokenCategory.Distribution)]
    [InlineData("Kiosk, E3 2003", TokenCategory.DevStatus)]
    public void Vocabulary_decides_classification_not_shape(string token, TokenCategory expected) =>
        Assert.Equal(expected, NamingVocabularyLoader.Load(TestFixtures.ShippedNamingDirectory()).Classify(token).Category);
}
