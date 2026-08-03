namespace ARK.Core.Hashing;

/// <summary>Why a ROM could not be hashed, or a caveat on the hash that was produced.</summary>
public enum RomReadStatus
{
    /// <summary>Hashed cleanly.</summary>
    Ok = 0,

    /// <summary>The archive or file could not be opened or read.</summary>
    Unreadable,

    /// <summary>
    /// The archive holds no entry, or more than one. Hashing never guesses which entry is the
    /// game; such archives are already reported by scan.
    /// </summary>
    NotSingleEntry,

    /// <summary>
    /// The file's timestamp or length moved while it was being read, so the bytes hashed describe
    /// a state that no longer exists. The hash is discarded and never cached.
    /// </summary>
    ChangedDuringRead,
}

/// <summary>
/// The hash of one ROM, with what was hashed and how it was reached.
/// </summary>
/// <param name="Crc32">CRC32 of the hashed bytes, lower-case hex.</param>
/// <param name="Sha1">SHA1, when computed. Null when only CRC32 was needed.</param>
/// <param name="Size">Length in bytes of what was hashed, after any normalization.</param>
/// <param name="Format">Format detected from the leading bytes.</param>
/// <param name="Normalized">
/// True when bytes were normalized in memory before hashing — a copier header skipped or a byte
/// order corrected. The file on disk is never altered.
/// </param>
public sealed record RomHash(string Crc32, string? Sha1, long Size, RomFormatDetection Format, bool Normalized);

/// <summary>The outcome of hashing one ROM.</summary>
/// <param name="Status">Whether a usable hash was produced.</param>
/// <param name="Hash">The hash, or null unless <see cref="Status"/> is <see cref="RomReadStatus.Ok"/>.</param>
/// <param name="Error">Why, when there is no hash.</param>
public sealed record RomHashResult(RomReadStatus Status, RomHash? Hash, string? Error)
{
    /// <summary>True when a usable hash was produced.</summary>
    public bool Succeeded => Status == RomReadStatus.Ok && Hash is not null;

    /// <summary>Builds a failed result.</summary>
    public static RomHashResult Failed(RomReadStatus status, string error) => new(status, null, error);
}

/// <summary>What a hash is being computed for, which decides how much hashing is enough.</summary>
public enum HashPurpose
{
    /// <summary>
    /// Comparing against one expected DAT entry. CRC32 plus size is sufficient — the collision
    /// risk against a single known value is negligible.
    /// </summary>
    Verification = 0,

    /// <summary>
    /// Finding which of 1.5M entries this is. 1.5M values in a 32-bit space carries real
    /// birthday-collision probability, so SHA1 is computed to settle ties.
    /// </summary>
    Identification,
}
