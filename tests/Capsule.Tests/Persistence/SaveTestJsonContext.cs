using System.Text.Json.Serialization;
using Capsule.Input;

namespace Capsule.Tests.Persistence;

// A game's settings as a spec sees them: a mutable class, so a test can mutate what it wrote and
// what it read and prove neither reaches the store.
public sealed class Settings
{
    public int Volume { get; set; } = 7;

    public string Name { get; set; } = "";
}

// A document carrying a rebindable button, proving the generated context picks up
// InputButtonJsonConverter from the type attribute with no converter registered here.
public sealed class ButtonHolder
{
    public InputButton Button { get; set; }
}

// A context declaring the platform newline and its own indentation, which the store must ignore:
// the document is the store's writer's, LF and two-space, whatever the game's context says.
[JsonSourceGenerationOptions(WriteIndented = true, NewLine = "\r\n", IndentSize = 4)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(ButtonHolder))]
internal sealed partial class SaveTestJsonContext : JsonSerializerContext;
