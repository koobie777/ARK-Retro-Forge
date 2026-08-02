namespace ARK.Core.Naming;

/// <summary>
/// The classification assigned to a single parenthetical token. Membership is decided by
/// <see cref="TokenVocabulary"/> — never by the token's position or separator shape, both of
/// which mislead on real data: <c>(USA, Europe)</c> and <c>(Virtual Console, Switch Online)</c>
/// are structurally identical and only vocabulary tells them apart.
/// </summary>
public enum TokenCategory
{
    /// <summary>Unrecognized. Preserved, flagged and surfaced — never dropped.</summary>
    Unknown = 0,

    /// <summary>Release region, or a comma+space list of them. The title/metadata boundary.</summary>
    Region,

    /// <summary>Language codes, comma separated with no space, optionally locale-suffixed.</summary>
    Language,

    /// <summary>Compilation or re-release collection the entry was sourced from.</summary>
    Compilation,

    /// <summary>Disc, disk, CD, side or tape ordinal within a multi-part release.</summary>
    Disc,

    /// <summary>Version number. A scheme entirely separate from <see cref="Revision"/>.</summary>
    Version,

    /// <summary>Revision. Absence means Rev 0 — the original, and the oldest.</summary>
    Revision,

    /// <summary>Pre-release build state. Retail carries no tag and outranks all of these.</summary>
    DevStatus,

    /// <summary>Build or release date. The only reliable ordering signal between pre-release builds.</summary>
    Date,

    /// <summary>Release, mode or dump variant that is not a pre-release build state.</summary>
    Edition,

    /// <summary>Licensing status. A separate policy axis from <see cref="DevStatus"/>.</summary>
    Licensing,

    /// <summary>Re-release or distribution channel.</summary>
    Distribution,

    /// <summary>Publisher or attribution appearing in a metadata slot.</summary>
    Publisher,

    /// <summary>Catalogue serial, e.g. <c>SLUS-00612</c>.</summary>
    Serial,

    /// <summary>Cartridge capability or PCB design. Never a dedup or variant signal.</summary>
    Hardware,

    /// <summary>Non-game content. Excluded from variant grouping entirely.</summary>
    NonGame,
}
