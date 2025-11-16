using ARK.Cli.Infrastructure;
using ARK.Core.Dat;
using Spectre.Console;

namespace ARK.Cli.Commands.Dat;

public static class DatSyncCommand
{
    private static readonly string CatalogPath = Path.Combine(AppContext.BaseDirectory, "config", "dat", "dat-sources.json");

    public static async Task<int> RunAsync(string[] args)
    {
        var system = GetArgValue(args, "--system");
        var force = args.Contains("--force");

        DatCatalog catalog;
        try
        {
            catalog = DatCatalog.Load(CatalogPath);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load DAT catalog ({CatalogPath}): {ex.Message}[/]");
            return (int)ExitCode.GeneralError;
        }

        var sources = catalog.FilterBySystem(system);
        if (sources.Count == 0)
        {
            AnsiConsole.MarkupLine(system == null
                ? "[yellow]No DAT sources defined in catalog.[/]"
                : $"[yellow]No DAT sources defined for system '{system}'.[/]");
            return (int)ExitCode.OK;
        }

        RenderPreview(sources);

        var downloader = new DatDownloader();
        var instanceRoot = InstancePathResolver.GetInstanceRoot();
        var datRoot = Path.Combine(instanceRoot, "dat");

        var results = new List<DatDownloadResult>();

        await AnsiConsole.Status()
            .StartAsync("Syncing DAT catalog...", async ctx =>
            {
                foreach (var source in sources)
                {
                    ctx.Status = $"Downloading [cyan]{source.Name}[/]";
                    var systemDirectory = Path.Combine(datRoot, source.System);
                    try
                    {
                        var result = await downloader.DownloadAsync(source, systemDirectory, force);
                        results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]  Failed to download {source.Name}: {ex.Message}[/]");
                    }
                }
            });

        var downloaded = results.Count(r => r.IsDownloaded);
        var skipped = results.Count - downloaded;

        AnsiConsole.MarkupLine($"[green]DAT sync complete[/] (Downloaded: {downloaded}, Skipped: {skipped})");
        AnsiConsole.MarkupLine($"Files stored under: [dim]{datRoot.EscapeMarkup()}[/]");

        return (int)ExitCode.OK;
    }

    private static void RenderPreview(IReadOnlyList<DatSourceDefinition> sources)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("System");
        table.AddColumn("Name");
        table.AddColumn("Format");
        table.AddColumn("Source");

        foreach (var source in sources)
        {
            table.AddRow(
                source.System.ToUpperInvariant(),
                source.Name,
                source.Format ?? "unknown",
                source.Author ?? "community");
        }

        AnsiConsole.Write(table);
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
