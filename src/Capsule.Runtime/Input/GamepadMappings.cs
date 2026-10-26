using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Input;

// Teaches SDL the controllers it cannot map, before any pad is opened. The community database ships
// embedded, gzipped at build time from gamecontrollerdb.txt beside this file, and only fills gaps. A
// mapping added at run time outranks SDL's own, and a pad that works on SDL alone keeps the mapping SDL
// gave it. A gamecontrollerdb.txt the player puts beside the game is read whole after it and
// overrides both. SDL's own environment hints hold a higher priority than either and keep working.
internal static class GamepadMappings
{
    // The file name a player's mappings ship under, at the content root.
    internal const string PlayerFile = "gamecontrollerdb.txt";

    internal const string ResourceName = "Capsule.Runtime.Input.gamecontrollerdb.txt.gz";

    private const string PlatformField = "platform:";

    // The driver signatures whose devices SDL maps itself: HIDAPI, XInput, RawInput, Windows.Gaming.Input,
    // GameInput, MFi and virtual. Byte 14 of an SDL GUID is the signature SDL_IsJoystickHIDAPI and its
    // kin read, two hex digits at offset 28 of the GUID's text.
    private const string DriverSignatures = "hxrwgmv";

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    // openContent reads the player's file, and null reads none. Called once, at boot, which allocates
    // the decompressed database once.
    internal static void Load(Func<string, Stream>? openContent)
    {
        (int added, int known) = FillGaps();
        Log.Info($"controller database: {added} mappings added, {known} left to SDL's own");

        if (openContent is null)
        {
            return;
        }

        Stream player;
        try
        {
            player = openContent(PlayerFile);
        }
        catch (IOException missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        using (player)
        {
            int read = AddAll(player);
            if (read > 0)
            {
                Log.Info($"{read} gamepad mappings read from {PlayerFile} beside the game");
            }
        }
    }

    // Each embedded line for this platform whose GUID no driver of SDL's maps and SDL's tables lack.
    // Returns how many were added, and how many were left to SDL because it maps their GUID already.
    private static unsafe (int Added, int Known) FillGaps()
    {
        string platform = Sdl3.Platform();
        int added = 0;
        int known = 0;

        using Stream compressed = typeof(GamepadMappings).Assembly.GetManifestResourceStream(ResourceName)!;
        using StreamReader lines = new(new GZipStream(compressed, CompressionMode.Decompress));
        while (lines.ReadLine() is { } line)
        {
            if (line.Length < 33 || line[0] == '#')
            {
                continue;
            }

            // A line keyed by a name, not a GUID, is one of SDL's own: "xinput" replaces the mapping
            // every XInput device shares.
            if (line[32] != ',' || line.AsSpan(0, 32).ContainsAnyExcept(HexDigits))
            {
                known++;
                continue;
            }

            string guid = line[..32];
            if (!ForPlatform(line, platform))
            {
                continue;
            }

            if (DrivenBySdl(guid) || Mapped(guid))
            {
                known++;
                continue;
            }

            if (Sdl3.SDL_AddGamepadMapping(line) < 0)
            {
                Log.Warning($"A line of the embedded controller database was not read: {Sdl3.Error()}");
                continue;
            }

            added++;
        }

        return (added, known);
    }

    // A line names its platform, as SDL reads it, or applies to every platform when it names none. SDL
    // takes the older "Mac OS X" for macOS.
    internal static bool ForPlatform(string line, string platform)
    {
        int field = line.IndexOf(PlatformField, StringComparison.Ordinal);
        if (field < 0)
        {
            return true;
        }

        ReadOnlySpan<char> named = line.AsSpan(field + PlatformField.Length);
        int end = named.IndexOf(',');
        named = end < 0 ? named : named[..end];

        return named.Equals(platform, StringComparison.OrdinalIgnoreCase)
            || (platform == "macOS" && named.Equals("Mac OS X", StringComparison.OrdinalIgnoreCase));
    }

    private static bool DrivenBySdl(string guid) =>
        byte.TryParse(guid.AsSpan(28, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte signature)
        && DriverSignatures.Contains((char)signature, StringComparison.Ordinal);

    private static bool Mapped(string guid)
    {
        nint mapping = Sdl3.SDL_GetGamepadMappingForGUID(Sdl3.SDL_StringToGUID(guid));
        if (mapping == nint.Zero)
        {
            return false;
        }

        Sdl3.SDL_free(mapping);
        return true;
    }

    // Every mapping in mappings, as SDL reads a file. 0 when it refused them.
    private static unsafe int AddAll(Stream mappings)
    {
        using MemoryStream copy = new();
        mappings.CopyTo(copy);
        if (copy.Length == 0)
        {
            return 0;
        }

        int added;
        fixed (byte* text = copy.GetBuffer())
        {
            nint stream = Sdl3.SDL_IOFromConstMem(text, (nuint)copy.Length);
            added = stream == nint.Zero ? -1 : Sdl3.SDL_AddGamepadMappingsFromIO(stream, closeStream: true);
        }

        if (added < 0)
        {
            Log.Warning($"The gamepad mappings in {PlayerFile} beside the game were not read, and the controllers they name may not read as gamepads: {Sdl3.Error()}");
            return 0;
        }

        return added;
    }
}
