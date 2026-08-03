using System.Text.Json;
using System.Text.Json.Serialization;

namespace ARK.Core.Serialization;

/// <summary>
/// The single source of JSON settings for everything ARK persists or emits.
/// </summary>
/// <remarks>
/// <para>
/// Centralized because of a defect found the hard way: the hash cache stored a <c>RomFormat</c> as
/// its integer ordinal, and deleting one enum member silently relabelled 592 cached rows. The
/// journal has the same exposure and far worse consequences — if <c>ActionKind</c> persisted as an
/// ordinal, adding a member would make undo replay a session as the wrong operations, turning the
/// safety net into the hazard with no signal until the damage was visible.
/// </para>
/// <para>
/// So every enum is written by <b>name</b>, and an architecture test asserts that no other type
/// constructs its own <see cref="JsonSerializerOptions"/>. Getting this wrong once is silent; the
/// rule makes it structural rather than a matter of remembering.
/// </para>
/// </remarks>
public static class ArkJson
{
    /// <summary>Options for anything ARK writes: indented, enums by name.</summary>
    public static JsonSerializerOptions Write { get; } = Build(indented: true);

    /// <summary>Options for reading ARK's own files and hand-authored config.</summary>
    public static JsonSerializerOptions Read { get; } = Build(indented: false);

    private static JsonSerializerOptions Build(bool indented) => new()
    {
        WriteIndented = indented,
        PropertyNameCaseInsensitive = true,
        Converters = { new TolerantEnumConverter() },
    };
}

/// <summary>
/// Writes enums by name, and reads a name this build does not recognize as that enum's
/// <c>Unknown</c> member rather than throwing or silently picking whatever sits at zero.
/// </summary>
/// <remarks>
/// An enum with no <c>Unknown</c> member gets no fallback — an unrecognized name throws. Falling
/// back to the zero value would be actively dangerous where zero means something real:
/// <c>VerificationState.Verified</c> and <c>ScanBucket.Identified</c> are both zero, and quietly
/// decoding an unknown state as "verified" is the worst possible failure.
/// </remarks>
public sealed class TolerantEnumConverter : JsonConverterFactory
{
    private const string UnknownMember = "Unknown";

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsEnum;
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(typeof(Inner<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class Inner<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Numbers are refused outright: an ordinal in a persisted file is exactly the shape of
            // the defect this converter exists to prevent.
            if (reader.TokenType == JsonTokenType.Number)
            {
                throw new JsonException(
                    $"'{typeof(TEnum).Name}' was persisted as a number. Enums must be stored by name.");
            }

            return Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToString());
        }

        // Enums used as dictionary keys go through these instead, and would otherwise fall back
        // to the default numeric key — reintroducing ordinals through the side door.
        public override TEnum ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Parse(reader.GetString());

        public override void WriteAsPropertyName(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WritePropertyName(value.ToString());
        }

        private static TEnum Parse(string? text)
        {
            if (Enum.TryParse<TEnum>(text, ignoreCase: true, out var value))
            {
                return value;
            }

            if (Enum.TryParse<TEnum>(UnknownMember, ignoreCase: true, out var unknown))
            {
                return unknown;
            }

            throw new JsonException(
                $"'{text}' is not a known {typeof(TEnum).Name}, and {typeof(TEnum).Name} declares no Unknown member.");
        }
    }
}
