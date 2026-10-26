using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Capsule.Diagnostics;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Input;

// Teaches SDL the controllers it cannot map. A gamecontrollerdb.txt the player puts beside the game is
// read whole at boot. The community database ships embedded, one gzipped file per desktop platform
// split at build time from gamecontrollerdb.txt beside this file, and is read only when a connected
// joystick has no mapping. It only fills gaps: a GUID SDL or the player's file already maps keeps that
// mapping. SDL's own environment hints hold a higher priority than either and keep working.
internal static class GamepadMappings
{
    // The file name a player's mappings ship under, at the content root.
    internal const string PlayerFile = "gamecontrollerdb.txt";

    // The driver signatures whose devices SDL maps itself: HIDAPI, XInput, RawInput, Windows.Gaming.Input,
    // GameInput, MFi and virtual. Byte 14 of an SDL GUID is the signature SDL_IsJoystickHIDAPI and its
    // kin read, two hex digits at offset 28 of the GUID's text.
    private const string DriverSignatures = "hxrwgmv";

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    // The embedded database's resource for SDL's platform name, or null on a platform it has none for.
    internal static string? ResourceFor(string platform) => platform switch
    {
        "Windows" => "Capsule.Runtime.Input.gamecontrollerdb.windows.txt.gz",
        "Linux" => "Capsule.Runtime.Input.gamecontrollerdb.linux.txt.gz",
        "macOS" => "Capsule.Runtime.Input.gamecontrollerdb.macos.txt.gz",
        _ => null,
    };

    // Reads the player's file through openContent, and nothing when it is null or the file is missing.
    internal static void LoadPlayerFile(Func<string, Stream>? openContent)
    {
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

    // Adds each embedded line whose GUID no driver of SDL's maps and no mapping covers yet. Called once,
    // which allocates the decompressed database once.
    internal static void FillGaps()
    {
        string? resource = ResourceFor(Sdl3.Platform());
        using Stream? compressed = resource is null ? null : typeof(GamepadMappings).Assembly.GetManifestResourceStream(resource);
        if (compressed is null)
        {
            Log.Info("controller database: none embedded for this platform");
            return;
        }

        int added = 0;
        int known = 0;
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

        Log.Info($"controller database: {added} mappings added, {known} left to SDL's own");
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
