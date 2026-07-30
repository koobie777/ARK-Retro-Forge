using ARK.Core.Dat;
using ARK.Core.Instances;
using ARK.Core.Systems;
using ARK.Core.Tools;

namespace ARK.Core.Diagnostics;

/// <summary>
/// Gathers a <see cref="MedicalBayReport"/> from Core services. A pure query: it reads instance,
/// tool, system, and DAT status and returns one object. It never mutates the filesystem, builds no
/// plan, touches no console, and reaches no pass/fail verdict — rendering and any decisions belong to
/// the caller. This service is the boundary-shape every later Core component copies.
/// </summary>
public sealed class MedicalBayService
{
    private const string UnrecognizedSystem = "(unrecognized)";

    private readonly InstancePaths _paths;
    private readonly ToolManager _tools;
    private readonly DatCatalog _catalog;
    private readonly SystemRegistry _systems;

    /// <summary>Creates the service over the instance, tool, DAT, and system sources it reports on.</summary>
    public MedicalBayService(InstancePaths paths, ToolManager tools, DatCatalog catalog, SystemRegistry systems)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(systems);
        _paths = paths;
        _tools = tools;
        _catalog = catalog;
        _systems = systems;
    }

    /// <summary>Builds the report for the given runtime context.</summary>
    public MedicalBayReport Generate(MedicalBayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var definition = _systems.Resolve(context.SystemCode);
        return new MedicalBayReport
        {
            InstanceName = _paths.InstanceName,
            RomRoot = context.RomRoot,
            RomRootSet = !string.IsNullOrWhiteSpace(context.RomRoot),
            ActiveSystem = new ActiveSystemStatus
            {
                Code = context.SystemCode,
                Recognized = definition is not null,
                DisplayName = definition?.DisplayName
            },
            Tools = _tools.CheckAllTools().ToList(),
            DatCatalogs = _catalog.Coverage()
                .Select(coverage => new DatStatus
                {
                    System = coverage.System ?? UnrecognizedSystem,
                    DatName = coverage.DatName,
                    EntryCount = coverage.EntryCount,
                    Version = coverage.Version,
                    Date = coverage.Date
                })
                .ToList()
        };
    }
}
