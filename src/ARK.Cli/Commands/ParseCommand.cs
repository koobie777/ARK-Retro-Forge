using System.CommandLine;
using ARK.Cli.Rendering;
using ARK.Core.Naming;
using Spectre.Console;

namespace ARK.Cli.Commands;

/// <summary>
/// Wires the <c>parse</c> verb: prints the token breakdown for one filename. Presentation only.
/// </summary>
/// <remarks>
/// This is the debugging surface for every phase after naming. When a file is not identified, or
/// is grouped with something it should not be, this verb shows exactly which tokens the tokenizer
/// saw and what the canonical reassembly looks like — without needing a DAT, a drive, or a scan.
/// </remarks>
public static class ParseCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(IAnsiConsole console, NameTokenizer tokenizer, NameFormatter formatter, TokenVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var nameArgument = new Argument<string>("name")
        {
            Description = "Filename to tokenize. The extension, if any, is ignored."
        };

        var command = new Command("parse", "Show the token breakdown for one filename.");
        command.Add(nameArgument);

        command.SetAction(parseResult =>
        {
            var input = parseResult.GetValue(nameArgument) ?? string.Empty;
            var parsed = tokenizer.Parse(StripExtension(input));

            ParseRenderer.Render(console, parsed, formatter, vocabulary);

            // A name that could not be tokenized is a non-zero exit: scripts should be able to
            // tell "flagged, needs a human" from "parsed cleanly".
            return parsed.IsTokenizable ? 0 : 1;
        });

        return command;
    }

    private static string StripExtension(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot > 0 && value.Length - dot <= 5 ? value[..dot] : value;
    }
}
