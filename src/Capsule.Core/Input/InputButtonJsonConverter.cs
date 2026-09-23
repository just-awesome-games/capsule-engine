using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Input;

/// <summary>
/// Reads and writes an <see cref="InputButton"/> as the string its <c>ToString</c> writes. The type's
/// <c>[JsonConverter]</c> attribute names it, and a game never registers it.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InputButtonJsonConverter : JsonConverter<InputButton>
{
    /// <inheritdoc/>
    public override InputButton Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A {nameof(InputButton)} is a JSON string. {InputButton.ExpectedShape}");
        }

        string text = reader.GetString()!;
        if (!InputButton.TryParse(text, out InputButton button))
        {
            throw new JsonException($"'{text}' is not a valid {nameof(InputButton)}. {InputButton.ExpectedShape}");
        }

        return button;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, InputButton value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
