namespace ARK.Core.Scanning;

/// <summary>
/// One directory's immediate file listing. Subdirectories are listed separately rather than
/// rolled up, because classification is per-directory: a drive mixes ROM sets with tooling at
/// every level, and a parent's verdict says nothing about its children.
/// </summary>
/// <param name="FullPath">Absolute path of the directory.</param>
/// <param name="Name">Leaf directory name — carries format qualifiers such as <c>(BigEndian)</c>.</param>
/// <param name="Files">Files directly inside this directory.</param>
/// <param name="SubdirectoryCount">
/// How many subdirectories it holds. Only used to tell a container apart from a genuinely empty
/// directory, so a scan root that holds nothing but folders is not reported as "empty".
/// </param>
public sealed record DirectoryListing(
    string FullPath,
    string Name,
    IReadOnlyList<FileEntry> Files,
    int SubdirectoryCount = 0);
