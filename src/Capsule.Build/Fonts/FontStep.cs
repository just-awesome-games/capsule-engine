using System.Text;

namespace Capsule.Build.Fonts;

/// <summary>
/// Reads every bitmap font and declares it as a <c>BitmapFont</c>, and sets
/// <see cref="BuildPass.FontPages"/>. The textures a font names beside it are its pages. A misspelt
/// font is a compile error and nothing is parsed at run time.
/// </summary>
internal static class FontStep
{
    // A byte that is no UTF-8 fails the font instead of reading as a replacement character.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static void Run(BuildPass pass)
    {
        HashSet<string> pages = new(StringComparer.Ordinal);

        foreach ((Source font, (BmFontDescription Description, Source[] Pages) model) in pass.Each(
            pass.Of(AssetType.Fonts),
            font =>
            {
                BmFontDescription description = BmFontParser.Parse(File.ReadAllText(font.Path, StrictUtf8));
                Source[] fontPages = [.. description.PageFiles.Select(file => Page(pass, font, file))];
                pass.Progress("fonts", font);

                return (description, fontPages);
            }))
        {
            pages.UnionWith(model.Pages.Select(static page => page.Key));
            pass.Declare(font, model, FontMembers.Write);
        }

        pass.FontPages = pages;
    }

    // The texture a page file names, resolved beside the font.
    private static Source Page(BuildPass pass, Source font, string file)
    {
        string path = Path.Combine(Path.GetDirectoryName(font.Path)!, file).Replace('\\', '/');
        string below = Keys.Below(pass.Requests.AssetRoot, path);
        string subject = $"names page \"{file}\"";

        return pass.Textures.TryGetValue(Keys.Of(below[..^Path.GetExtension(below).Length], subject), out Source texture)
            ? texture
            : throw new FormatException(
                $"{subject}, and this game authors no texture at \"{path}\". Author the page beside the font and keep it out of a development-only directory.");
    }
}
