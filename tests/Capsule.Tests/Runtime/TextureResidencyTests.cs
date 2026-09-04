using Capsule.Assets;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Runtime;

// Where a handle's file has to be, and what boot says when it is not there. The decode itself
// needs a graphics device; the path contract does not, and it is the half a game gets wrong.
public sealed class TextureResidencyTests
{
    private static readonly TextureHandle Hero = new("hero", ".png");

    private static readonly TextureHandle Tiles = new("tiles", ".png");

    [Fact]
    public void AHandle_NamesItsFileUnderTheTexturesDomain()
    {
        Assert.Equal("assets/textures/hero.png", TextureFiles.RelativePathOf(Hero));
    }

    // A handle's name is the source's path under the textures root, so a nested asset resolves to a
    // nested file — with the format's separator, whatever the platform's is.
    [Fact]
    public void ANestedHandle_NamesItsFileUnderTheDirectoryItWasAuthoredIn()
    {
        TextureHandle bat = new("enemies/bat", ".png");
        using Shipped shipped = new(bat);

        Assert.Equal("assets/textures/enemies/bat.png", TextureFiles.RelativePathOf(bat));
        Assert.Equal(shipped.Path, TextureFiles.Locate(shipped.BaseDirectory, bat));
    }

    [Fact]
    public void Locate_FindsAShippedTexture()
    {
        using Shipped shipped = new(Hero);

        Assert.Equal(shipped.Path, TextureFiles.Locate(shipped.BaseDirectory, Hero));
    }

    [Fact]
    public void Locate_FailsNamingTheHandleAndThePathItLookedIn()
    {
        using Shipped shipped = new();

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => TextureFiles.Locate(shipped.BaseDirectory, Hero));

        Assert.Contains("'hero'", error.Message, StringComparison.Ordinal);
        Assert.Contains("assets/textures/hero.png", error.Message, StringComparison.Ordinal);
    }

    // Registries aggregate per logic assembly, so two of them may ship under one stem. That names
    // one file, and decoding it twice would strand the first texture on the device.
    [Fact]
    public void Resolve_LocatesEachHandleOnce_InFirstAppearanceOrder()
    {
        using Shipped shipped = new(Hero, Tiles);

        (TextureHandle Handle, string Path)[] resolved =
            TextureFiles.Resolve(shipped.BaseDirectory, [Tiles, Hero, Tiles]);

        Assert.Equal([Tiles, Hero], resolved.Select(static entry => entry.Handle));
        Assert.All(resolved, entry => Assert.True(File.Exists(entry.Path)));
    }

    [Fact]
    public void Resolve_FailsOnTheFirstHandleThatShipsNoFile()
    {
        using Shipped shipped = new(Hero);

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => TextureFiles.Resolve(shipped.BaseDirectory, [Hero, Tiles]));

        Assert.Contains("'tiles'", error.Message, StringComparison.Ordinal);
    }

    private sealed class Shipped : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("capsule-textures-");

        internal Shipped(params TextureHandle[] textures)
        {
            foreach (TextureHandle handle in textures)
            {
                Path = System.IO.Path.Combine(BaseDirectory, TextureFiles.RelativePathOf(handle));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllBytes(Path, []);
            }
        }

        internal string BaseDirectory => _directory.FullName;

        /// <summary>The last file shipped, which is the only one the single-handle specs ship.</summary>
        internal string Path { get; private set; } = string.Empty;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
