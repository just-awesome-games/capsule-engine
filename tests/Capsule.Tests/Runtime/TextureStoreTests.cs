using Capsule.Assets;
using Capsule.Runtime.Assets;

namespace Capsule.Tests.Runtime;

// Where a handle's file has to be for the store to open it.
public sealed class TextureStoreTests
{
    private static readonly TextureHandle Hero = new("hero", ".png");

    // A handle's name is the source's path under Assets/. A nested asset resolves to a nested file,
    // with the format's separator whatever the platform's is. What the handle names is what Open
    // reads once the file ships there.
    [Theory]
    [InlineData("hero", "assets/hero.png")]
    [InlineData("textures/nested/sheet", "assets/textures/nested/sheet.png")]
    public void AHandle_NamesAndLocatesItsFileAtItsPath(string name, string expected)
    {
        TextureHandle handle = new(name, ".png");
        using Shipped shipped = new(handle);

        Assert.Equal(expected, TextureStore.RelativePathOf(handle));
        using Stream opened = TextureStore.Open(shipped.Platform, handle);
        Assert.Equal(System.IO.Path.GetFullPath(shipped.Path), Assert.IsType<FileStream>(opened).Name);
    }

    [Fact]
    public void Open_FailsNamingTheHandleAndThePathItLookedIn()
    {
        using Shipped shipped = new();

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(
            () => TextureStore.Open(shipped.Platform, Hero));

        Assert.Contains("'hero'", error.Message, StringComparison.Ordinal);
        Assert.Contains("assets/hero.png", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_RejectsAHandleThatWouldResolveOutsideTheContentRoot()
    {
        using Shipped shipped = new();
        string outside = System.IO.Path.Combine(shipped.BaseDirectory, "outside.png");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outside)!);
        File.WriteAllBytes(outside, []);
        TextureHandle escaping = new("../outside", ".png");

        Assert.Throws<ArgumentException>(() => TextureStore.Open(shipped.Platform, escaping));
    }

    [Fact]
    public void Open_RejectsTheDefaultHandleBeforeLookingForAFile()
    {
        using Shipped shipped = new();

        Assert.Throws<ArgumentException>(() => TextureStore.Open(shipped.Platform, default));
    }

    private sealed class Shipped : IDisposable
    {
        private readonly TempWorkspace _workspace = new("capsule-textures-");

        /// <summary>The last file shipped, which is the only one the single-handle specs ship.</summary>
        internal string Path { get; private set; } = string.Empty;

        internal Shipped(params TextureHandle[] textures)
        {
            foreach (TextureHandle handle in textures)
            {
                Path = _workspace.PathTo(TextureStore.RelativePathOf(handle));
                File.WriteAllBytes(Path, []);
            }
        }

        internal string BaseDirectory => _workspace.Root;

        internal ContentPlatform Platform => new(_workspace.Root);

        public void Dispose() => _workspace.Dispose();
    }
}
