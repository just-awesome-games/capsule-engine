namespace Capsule.Input;

// The characters a US layout prints on each key, uppercased, indexed by key, and null for a key it
// prints no character on. A driven or headless run reads keys by it.
internal static class KeyCharacters
{
    internal static readonly string?[] Us = Build();

    private static string?[] Build()
    {
        // GetValues sorts by value. The last key is the highest.
        Key[] keys = Enum.GetValues<Key>();
        string?[] characters = new string?[(int)keys[^1] + 1];
        foreach (Key key in keys)
        {
            characters[(int)key] = Of(key);
        }

        return characters;
    }

    private static string? Of(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.Digit0 and <= Key.Digit9 => ((char)('0' + (key - Key.Digit0))).ToString(),
        Key.Grave => "`",
        Key.Minus => "-",
        Key.Equals => "=",
        Key.LeftBracket => "[",
        Key.RightBracket => "]",
        Key.Backslash => "\\",
        Key.Semicolon => ";",
        Key.Apostrophe => "'",
        Key.Comma => ",",
        Key.Period => ".",
        Key.Slash => "/",
        _ => null,
    };
}
