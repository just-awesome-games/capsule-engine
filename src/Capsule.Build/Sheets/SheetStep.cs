using Capsule.Build.Caching;

namespace Capsule.Build.Sheets;

/// <summary>
/// Reads every sheet and declares it as a class of typed frames, clips and sockets. A misspelt frame,
/// clip or socket is a compile error, and no sheet ships beside the executable.
/// </summary>
internal static class SheetStep
{
    private const string Step = "sheets";

    internal static void Run(PipelinePass pass)
    {
        foreach ((Source sheet, Sheet document) in pass.Each(
            Step,
            pass.Of(AssetType.Sheets),
            source => Derivation.Of(source),
            (source, _) => SheetFile.Read(source.Path),
            DerivationCacheJsonContext.Default.Sheet))
        {
            // Resolved by key against what the build ships, and carrying the shipped extension,
            // however the sheet spelled it. Checked on every run, since the texture is not the sheet's.
            if (!pass.Textures.TryGetValue(document.TextureKey, out Source texture))
            {
                pass.Fail(
                    sheet.Path,
                    $"cuts from texture \"{document.TextureKey}{document.TextureExtension}\", which this game does not ship. Author it at Assets/{document.TextureKey}{document.TextureExtension}.");
                continue;
            }

            pass.Declare(sheet, document with { TextureExtension = texture.Extension }, SheetMembers.Write);
        }
    }
}
