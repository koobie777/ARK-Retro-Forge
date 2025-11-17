using Spectre.Console;

namespace ARK.Cli.Infrastructure;

internal static class DatUsageHelper
{
    public static void WarnIfCatalogMissing(string system, string feature)
    {
        var summary = DatStatusReporter.Inspect(system).FirstOrDefault();
        var systemLabel = system.ToUpperInvariant();

        if (summary == null)
        {
            AnsiConsole.MarkupLine($"[yellow][[DAT]] {feature} works best with {systemLabel} catalogs, but the catalog index is offline. Run [green]dat sync --system {system}[/] when ready.[/]");
            return;
        }

        if (!summary.HasCatalog)
        {
            AnsiConsole.MarkupLine($"[yellow][[DAT]] {feature} detected no {systemLabel} DAT files. Run [green]dat sync --system {system}[/] to improve accuracy.[/]");
            return;
        }

        if (summary.IsStale)
        {
            AnsiConsole.MarkupLine($"[yellow][[DAT]] {feature} is using a stale {systemLabel} catalog (last sync {Format(summary.LastUpdatedUtc)}). Run [green]dat sync --system {system} --force[/] to refresh.[/]");
        }
    }

    private static string Format(DateTime? timestamp)
        => timestamp?.ToString("yyyy-MM-dd HH:mm") ?? "unknown";
}
