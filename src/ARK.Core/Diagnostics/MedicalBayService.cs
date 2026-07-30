using System.Diagnostics.CodeAnalysis;
using ARK.Core.Instances;
using ARK.Core.Systems;
using ARK.Core.Tools;

namespace ARK.Core.Diagnostics;

/// <summary>
/// Gathers a <see cref="MedicalBayReport"/> from Core services. A pure query: it reads instance,
/// tool, and DAT status and returns one object. It never mutates the filesystem, builds no plan,
/// touches no console, and reaches no pass/fail verdict — rendering and any decisions belong to the
/// caller. This service is the boundary-shape every later Core component copies.
/// </summary>
public sealed class MedicalBayService
{
    private readonly InstancePaths _paths;
    private readonly ToolManager _tools;
    private readonly DatStatusReporter _dat;

    /// <summary>Creates the service over the instance, tool, and DAT sources it reports on.</summary>
    public MedicalBayService(InstancePaths paths, ToolManager tools, DatStatusReporter dat)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(dat);
        _paths = paths;
        _tools = tools;
        _dat = dat;
    }

    /// <summary>Builds the report for the given runtime context.</summary>
    [RequiresUnreferencedCode("Inspects the DAT catalog, which uses reflection-based System.Text.Json.")]
    public MedicalBayReport Generate(MedicalBayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new MedicalBayReport
        {
            InstanceName = _paths.InstanceName,
            RomRoot = context.RomRoot,
            RomRootSet = !string.IsNullOrWhiteSpace(context.RomRoot),
            ActiveSystemProfile = SystemProfiles.Resolve(context.SystemCode),
            Tools = _tools.CheckAllTools().ToList(),
            DatCatalogs = _dat.Inspect().ToList()
        };
    }
}
