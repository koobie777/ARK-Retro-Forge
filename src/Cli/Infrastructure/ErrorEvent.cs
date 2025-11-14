namespace ARK.Cli.Infrastructure;

/// <summary>
/// Represents an error event with structured information
/// </summary>
public record ErrorEvent
{
    public required string Code { get; init; }
    public required string Component { get; init; }
    public required string Context { get; init; }
    public string? Suggestion { get; init; }

    public override string ToString()
    {
        var suggestion = !string.IsNullOrEmpty(Suggestion) ? $" | Fix: {Suggestion}" : string.Empty;
        return $"⚠️ {Code} | Component: {Component} | Context: {Context}{suggestion}";
    }
}
