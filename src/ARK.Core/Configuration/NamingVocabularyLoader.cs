using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ARK.Core.Naming;

namespace ARK.Core.Configuration;

/// <summary>
/// Materializes a <see cref="TokenVocabulary"/> from <c>config/naming/*.json</c>.
/// </summary>
/// <remarks>
/// This lives outside <c>Core/Naming</c> on purpose. Naming is pure string work and an
/// architecture test asserts that nothing under <c>Core/Naming</c> reads a file, so the one
/// place that does read files sits here instead. Adding a term to a config file — or dropping in
/// a new vocabulary file — changes classification with no recompile.
/// </remarks>
public static class NamingVocabularyLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Loads every <c>*.json</c> in <paramref name="namingDirectory"/>. A missing directory
    /// yields an empty vocabulary rather than throwing; files are read in name order so the
    /// result is deterministic.
    /// </summary>
    [RequiresUnreferencedCode("Deserializes NamingVocabularyDocument with reflection-based System.Text.Json.")]
    public static TokenVocabulary Load(string namingDirectory)
    {
        var documents = new List<NamingVocabularyDocument>();

        if (Directory.Exists(namingDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(namingDirectory, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
            {
                var document = JsonSerializer.Deserialize<NamingVocabularyDocument>(File.ReadAllText(file), JsonOptions);
                if (document is not null)
                {
                    documents.Add(document);
                }
            }
        }

        return TokenVocabulary.FromDocuments(documents);
    }
}
