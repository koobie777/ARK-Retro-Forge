namespace ARK.Core.Execution;

/// <summary>
/// The kind of filesystem mutation a <see cref="PlannedAction"/> describes.
/// </summary>
public enum ActionKind
{
    /// <summary>Relocate a file or game unit to a new location.</summary>
    Move,

    /// <summary>Change a file's name within its current directory.</summary>
    Rename,

    /// <summary>Move a removal candidate into the instance's quarantine area.</summary>
    Quarantine,

    /// <summary>Create a directory.</summary>
    CreateDirectory,

    /// <summary>Write text content to a file.</summary>
    WriteText
}
