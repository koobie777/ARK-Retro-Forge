using System.Text.Json.Serialization;

namespace ARK.Core.Dat;

public sealed record DatSourceDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("system")]
    public required string System { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>
    /// Optional URL to download cue sheet files associated with this DAT source.
    /// </summary>
    [JsonPropertyName("cueUrl")]
    public string? CueUrl { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
