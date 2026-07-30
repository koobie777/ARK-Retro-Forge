using Spectre.Console;

namespace ARK.Cli.Infrastructure;

internal static class ConsoleDecorations
{
    internal readonly record struct HeaderMetadata(string Label, string Value, bool IsMarkup = false);

    public static void RenderOperationHeader(string title, params HeaderMetadata[] metadata)
    {
        var table = new Table().HideHeaders();
        table.AddColumn("Key");
        table.AddColumn("Value");

        foreach (var entry in metadata)
        {
            if (string.IsNullOrWhiteSpace(entry.Label))
            {
                continue;
            }

            var label = Markup.Escape(entry.Label);
            var value = string.IsNullOrWhiteSpace(entry.Value)
                ? "-"
                : entry.IsMarkup
                    ? entry.Value
                    : Markup.Escape(entry.Value);

            table.AddRow($"[dim]{label}[/]", value);
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[bold cyan]{title}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("silver"),
            Padding = new Padding(1, 1)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
}
