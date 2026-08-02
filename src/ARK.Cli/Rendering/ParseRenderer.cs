using ARK.Core.Naming;
using Spectre.Console;

namespace ARK.Cli.Rendering;

/// <summary>Renders one <see cref="ParsedName"/>. Rendering only — it decides nothing.</summary>
public static class ParseRenderer
{
    /// <summary>Writes the title, each classified token, the unknown bucket, and the canonical form.</summary>
    public static void Render(IAnsiConsole console, ParsedName parsed, NameFormatter formatter, TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(vocabulary);

        if (!parsed.IsTokenizable)
        {
            console.MarkupLineInterpolated($"[yellow]Flagged:[/] {parsed.Flag}");
            console.MarkupLineInterpolated($"[grey]{parsed.Title}[/]");
            console.MarkupLine("[grey]No recognized region token, so the title/metadata boundary is undefined. Not guessed.[/]");
            return;
        }

        console.MarkupLineInterpolated($"[bold]Title[/]  {parsed.Title}");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Category");
        table.AddColumn("Token");

        foreach (var token in parsed.Tokens)
        {
            var colour = token.Category == TokenCategory.Unknown ? "yellow" : "green";
            table.AddRow($"[{colour}]{token.Category}[/]", Markup.Escape(token.Value));
        }

        foreach (var flag in parsed.BracketFlags)
        {
            table.AddRow("[blue]BracketFlag[/]", Markup.Escape($"[{flag}]"));
        }

        if (parsed.Tokens.Count > 0 || parsed.BracketFlags.Count > 0)
        {
            console.Write(table);
        }

        if (parsed.UnknownTokens.Count > 0)
        {
            console.MarkupLineInterpolated(
                $"[yellow]Unknown bucket:[/] {string.Join(", ", parsed.UnknownTokens.Select(token => token.Value))}");
            console.MarkupLine("[grey]Preserved and surfaced. Unknown tokens are how the vocabulary tables grow.[/]");
        }

        console.MarkupLineInterpolated($"[bold]Canonical[/]  {formatter.Format(parsed)}");
        console.MarkupLineInterpolated($"[grey]Match key   {NameMatchKey.For(parsed, vocabulary)}[/]");
    }
}
