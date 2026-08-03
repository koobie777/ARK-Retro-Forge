namespace ARK.Core.Execution;

/// <summary>
/// The kind of filesystem mutation a <see cref="PlannedAction"/> describes.
/// </summary>
public enum ActionKind
{
    /// <summary>
    /// A kind this build does not recognize, read from a journal written by another version.
    /// Present so an unknown name decodes to something explicit; undo refuses any session
    /// containing one rather than guessing what it meant.
    /// </summary>
    Unknown = 0,

    /// <summary>Relocate a file or game unit to a new location.</summary>
    Move,

    /// <summary>Change a file's name within its current directory.</summary>
    Rename,

    /// <summary>Move a removal candidate into the instance's quarantine area.</summary>
    Quarantine,

    /// <summary>Create a directory.</summary>
    CreateDirectory,

    /// <summary>Write text content to a file.</summary>
    WriteText,

    /// <summary>
    /// Remove a directory, and only when it is empty.
    /// </summary>
    /// <remarks>
    /// Produced by inversion only — never by an operation. Undoing a <see cref="CreateDirectory"/>
    /// has to be able to take the directory away again, but a directory the user has since filled
    /// is left alone: restoring "the prior state" never means discarding what arrived afterwards.
    /// </remarks>
    RemoveDirectory,

    /// <summary>
    /// Delete a file that did not exist before the session being reversed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Produced by inversion only — never by an operation, and never as a way of removing a
    /// removal candidate. Prohibition 5 stands: ARK's answer to "get rid of this" is quarantine.
    /// </para>
    /// <para>
    /// This exists solely so undo can restore the exact prior state. If a session created a file
    /// where there was none, the only faithful reversal is for that file to be gone again.
    /// </para>
    /// </remarks>
    DeleteFile
}
