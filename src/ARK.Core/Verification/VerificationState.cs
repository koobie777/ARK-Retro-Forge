namespace ARK.Core.Verification;

/// <summary>
/// The five states a unit can be in after verification.
/// </summary>
/// <remarks>
/// <see cref="InProgress"/> exists so that files still being written are not reported as damaged.
/// A file mid-download fails hash comparison, which is technically true and uselessly reported —
/// and sixty false corruption reports is how a user learns to stop reading the report. It is
/// therefore evaluated <b>before</b> <see cref="Mismatched"/>, never after.
/// </remarks>
public enum VerificationState
{
    /// <summary>Hash matches the DAT entry. The only state eligible for renaming.</summary>
    Verified = 0,

    /// <summary>
    /// Actively being written, carrying an incomplete-download extension, or inside a declared
    /// incomplete-download directory. <b>Not judged.</b>
    /// </summary>
    InProgress,

    /// <summary>
    /// Name matches a DAT entry, hash does not. Diagnosable, not unknown: this claims to be a
    /// specific release and the bytes disagree.
    /// </summary>
    Mismatched,

    /// <summary>No match by hash or by name.</summary>
    Unrecognized,

    /// <summary>Not ROM content.</summary>
    Excluded,
}
