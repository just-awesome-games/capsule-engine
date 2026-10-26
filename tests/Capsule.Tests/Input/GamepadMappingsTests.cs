using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Capsule.Runtime.Input;
using Capsule.Runtime.Platform;

namespace Capsule.Tests.Input;

[Collection(SdlEventsCollection.Name)]
public sealed class GamepadMappingsTests
{
    // The community database maps devices that SDL's own tables lack. A GUID SDL already maps keeps
    // SDL's mapping, and a GUID the player's file read at boot maps keeps the player's, though the
    // database is read after it.
    [Fact]
    public void TheEmbeddedDatabase_FillsOnlyGaps_LeavingSdlsAndThePlayersMappings()
    {
        string[] lines = [.. EmbeddedLines(Sdl3.Platform())];
        string filled = "";
        WithGamepads(() =>
        {
            string[] unknown = [.. lines.Where(static line => line[32] == ',' && Mapping(Guid(line)) is null)];
            Dictionary<string, string> known = lines.Select(static line => Guid(line)).Distinct().Select(static guid => (guid, mapping: Mapping(guid))).Where(static entry => entry.mapping is not null).ToDictionary(static entry => entry.guid, static entry => entry.mapping!);
            Assert.NotEmpty(unknown);
            Assert.NotEmpty(known);

            GamepadMappings.FillGaps();

            Assert.All(known, entry => Assert.Equal(entry.Value, Mapping(entry.Key)));
            filled = unknown.First(line => Mapping(Guid(line)) is not null);
        });

        string platform = filled[filled.IndexOf("platform:", StringComparison.Ordinal)..];
        byte[] player = Encoding.UTF8.GetBytes($"{Guid(filled)},Player Pad,a:b3,b:b2,{platform}\n");
        WithGamepads(() =>
        {
            GamepadMappings.LoadPlayerFile(name => name == GamepadMappings.PlayerFile ? new MemoryStream(player) : throw new FileNotFoundException(name));
            GamepadMappings.FillGaps();

            Assert.StartsWith($"{Guid(filled)},Player Pad,", Mapping(Guid(filled)), StringComparison.Ordinal);
        });
    }

    // Each desktop platform's embedded file holds only the lines that name it.
    [Theory]
    [InlineData("Windows", "Windows")]
    [InlineData("Linux", "Linux")]
    [InlineData("macOS", "Mac OS X")]
    public void EachPlatformsDatabase_HoldsOnlyItsOwnLines(string platform, string named)
    {
        string[] lines = [.. EmbeddedLines(platform)];

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(line.Contains($"platform:{named},", StringComparison.Ordinal) || line.EndsWith($"platform:{named}", StringComparison.Ordinal), line));
    }

    // SDL's gamepad mappings live while its gamepad subsystem is up and are dropped when it quits.
    private static void WithGamepads(Action body)
    {
        Assert.True(Sdl3.SDL_InitSubSystem(Sdl3.InitGamepad), Sdl3.Error());
        try
        {
            body();
        }
        finally
        {
            Sdl3.SDL_QuitSubSystem(Sdl3.InitGamepad);
        }
    }

    private static IEnumerable<string> EmbeddedLines(string platform)
    {
        using Stream compressed = typeof(GamepadMappings).Assembly.GetManifestResourceStream(GamepadMappings.ResourceFor(platform)!)!;
        using StreamReader reader = new(new GZipStream(compressed, CompressionMode.Decompress));

        return reader.ReadToEnd().Split('\n').Where(static line => line.Length > 32);
    }

    private static string Guid(string line) => line[..line.IndexOf(',', StringComparison.Ordinal)];

    // The mapping SDL resolves for guid, or null for none.
    private static string? Mapping(string guid)
    {
        nint mapping = Sdl3.SDL_GetGamepadMappingForGUID(Sdl3.SDL_StringToGUID(guid));
        if (mapping == nint.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(mapping);
        }
        finally
        {
            Sdl3.SDL_free(mapping);
        }
    }
}
