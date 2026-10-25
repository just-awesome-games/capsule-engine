using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Capsule.Runtime.Input;
using Capsule.Runtime.Sdl;

namespace Capsule.Tests.Input;

[Collection(SdlEventsCollection.Name)]
public sealed class GamepadMappingsTests
{
    // Raphnet's SNES adapters are in the community database and not in SDL's own. A GUID SDL already
    // maps keeps SDL's mapping. A line the player's file gives a GUID replaces whatever came before.
    [Fact]
    public void TheEmbeddedDatabase_FillsOnlySdlsGaps_AndThePlayersFileOverridesIt()
    {
        Assert.True(Sdl3.SDL_InitSubSystem(Sdl3.InitGamepad), Sdl3.Error());
        try
        {
            string[] lines = [.. EmbeddedLines().Where(static line => line.Length > 32 && line[0] != '#')];
            string[] unknown = [.. lines.Where(static line => line.Contains(",Raphnet SNES Adapter,", StringComparison.Ordinal) && Mapping(Guid(line)) is null)];
            Dictionary<string, string> known = lines.Select(static line => Guid(line)).Distinct().Select(static guid => (guid, mapping: Mapping(guid))).Where(static entry => entry.mapping is not null).ToDictionary(static entry => entry.guid, static entry => entry.mapping!);
            Assert.NotEmpty(unknown);
            Assert.NotEmpty(known);

            GamepadMappings.Load(openContent: null);

            Assert.All(known, entry => Assert.Equal(entry.Value, Mapping(entry.Key)));

            string mapped = unknown.First(line => Mapping(Guid(line)) is not null);
            string platform = mapped[mapped.IndexOf("platform:", StringComparison.Ordinal)..];
            byte[] player = Encoding.UTF8.GetBytes($"{Guid(mapped)},Player Pad,a:b3,b:b2,{platform}\n");

            GamepadMappings.Load(name => name == GamepadMappings.PlayerFile ? new MemoryStream(player) : throw new FileNotFoundException(name));

            Assert.StartsWith($"{Guid(mapped)},Player Pad,", Mapping(Guid(mapped)), StringComparison.Ordinal);
        }
        finally
        {
            Sdl3.SDL_QuitSubSystem(Sdl3.InitGamepad);
        }
    }

    private static IEnumerable<string> EmbeddedLines()
    {
        using Stream compressed = typeof(GamepadMappings).Assembly.GetManifestResourceStream(GamepadMappings.ResourceName)!;
        using StreamReader reader = new(new GZipStream(compressed, CompressionMode.Decompress));

        return reader.ReadToEnd().Split('\n');
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
