namespace ARK.Core.Verification;

/// <summary>Which signal marked a unit as still being written.</summary>
/// <remarks>
/// Ordered cheapest first. Every one of these is an <b>optimization that avoids hashing</b>, not
/// evidence of completeness: a real 0.99 GB file missing 16 MB was observed with no incomplete
/// extension and a full pre-allocated size, passing every cheap check. Only the hash catches that,
/// and only the mtime re-check catches a file changing underneath the hash.
/// </remarks>
public enum InProgressSignal
{
    /// <summary>Not in progress.</summary>
    None = 0,

    /// <summary>Carries a client's incomplete-transfer extension (<c>.part</c>, <c>.!qB</c>, …).</summary>
    IncompleteExtension,

    /// <summary>Sits inside a directory the user declared as an incomplete-download location.</summary>
    DeclaredDirectory,

    /// <summary>Written to within the recent-activity window.</summary>
    RecentWrite,

    /// <summary>
    /// Its timestamp or length moved while it was being hashed. The strongest signal of the four,
    /// and the only one that is proof rather than a hint.
    /// </summary>
    ChangedDuringRead,
}
