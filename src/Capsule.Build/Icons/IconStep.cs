using Capsule.Assets;
using Capsule.Build.Caching;
using Capsule.Build.Textures;

namespace Capsule.Build.Icons;

/// <summary>
/// Derives the application icon pair from <see cref="FileName"/> at the asset root, under <c>icon/</c>:
/// the executable's <c>Icon.ico</c> and the window's <c>Icon.bmp</c>.
/// </summary>
/// <remarks>
/// The source is no asset. It is never keyed, declared or shipped. A run with no source leaves
/// <c>icon/</c> empty, and the shell takes Capsule's default icons.
/// </remarks>
internal static class IconStep
{
    /// <summary>The source's name at the asset root.</summary>
    internal const string FileName = "Icon.png";

    /// <summary>The smallest side a source may have, the largest entry the <c>.ico</c> holds.</summary>
    internal const int SmallestSide = 256;

    /// <summary>The side of the window icon.</summary>
    internal const int WindowSide = 128;

    private const string Step = "icon";

    private const string Fix = "Export it square at 256x256 or larger. Pixel art scales up by a whole factor.";

    /// <summary>The side of every entry the <c>.ico</c> holds, smallest first.</summary>
    internal static readonly int[] Sides = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>Whether a source at <paramref name="below"/>, its path below the asset root, is the application icon.</summary>
    internal static bool IsIcon(string below) => string.Equals(below, FileName, Keys.PathComparison);

    internal static void Run(PipelinePass pass)
    {
        string root = pass.Requests.AssetRoot;
        IEnumerable<Request> sources = pass.Requests.Sources.Where(request =>
            request.Root is null
            && IsIcon(Keys.Below(root, request.Path))
            && !Keys.IsUnder(pass.DevelopmentOnly, FileName));

        pass.Each<Request, bool>(
            Step,
            sources,
            source => new Derivation(source.Path, [source.Path], $"sides={string.Join(',', Sides)}; window={WindowSide}", typeof(IconStep).Assembly),
            (source, files) =>
            {
                Derive(source.Path, files);

                return true;
            },
            facts: null,
            pass.Icon);
        pass.Icon.Prune();
    }

    private static void Derive(string path, DerivedFiles files)
    {
        Texels source = TexturePixels.Decode(path, TextureFormatSetting.Rgba);
        if (source.Width != source.Height)
        {
            throw new FormatException($"is {source.Width}x{source.Height} texels, and an application icon is square. {Fix}");
        }

        if (source.Width < SmallestSide)
        {
            throw new FormatException($"is {source.Width}x{source.Height} texels, under the {SmallestSide}x{SmallestSide} an application icon needs. {Fix}");
        }

        Texels[] entries = [.. Sides.Select(side => TexturePixels.Downscale(source, side, side))];
        files.Write("Icon.ico", temporary =>
        {
            using FileStream file = File.Create(temporary);
            IcoWriter.Write(entries, file);
        });
        files.Write("Icon.bmp", temporary =>
        {
            using FileStream file = File.Create(temporary);
            BmpWriter.Write(entries[Array.IndexOf(Sides, WindowSide)], file);
        });
    }
}
