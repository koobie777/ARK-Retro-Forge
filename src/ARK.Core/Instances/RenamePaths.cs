namespace ARK.Core.Instances;

/// <summary>
/// Resolves rename destinations, and is the sole authority for that composition.
/// </summary>
/// <remarks>
/// A path-resolution type beside <see cref="InstancePaths"/>, <see cref="QuarantinePaths"/>,
/// <see cref="SortPaths"/> and <see cref="OrganizePaths"/>, for the same reason: path composition
/// stays centralized so "nothing composes paths outside the resolver" remains enforceable.
/// </remarks>
public static class RenamePaths
{
    private const string TempMarker = ".ark-rename-tmp";

    /// <summary>
    /// Where a unit lands when renamed. A rename stays in its own directory — moving between
    /// directories is organizing, a different operation with a different report.
    /// </summary>
    public static string DestinationFor(string sourcePath, string proposedName) =>
        Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty, proposedName);

    /// <summary>
    /// A temporary name used to break a rename cycle, beside the destination so it never crosses a
    /// volume.
    /// </summary>
    public static string TempFor(string destination, int index) => destination + TempMarker + index;
}
