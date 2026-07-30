namespace ARK.Core.Settings;

/// <summary>
/// Thrown when a settings file exists but cannot be parsed. Callers must surface this rather than
/// fall back to defaults: silently resetting a user's configuration is a data-loss pattern.
/// </summary>
public sealed class SettingsFormatException : Exception
{
    /// <summary>Creates an empty exception.</summary>
    public SettingsFormatException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public SettingsFormatException(string? message) : base(message)
    {
    }

    /// <summary>Creates an exception with a message and inner exception.</summary>
    public SettingsFormatException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>Path of the settings file that failed to parse.</summary>
    public string? Path { get; init; }
}
