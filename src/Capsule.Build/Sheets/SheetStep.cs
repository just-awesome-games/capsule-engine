namespace Capsule.Build.Sheets;

/// <summary>
/// Reads every sheet and declares it as a class of typed frames, clips and sockets. A misspelt frame,
/// clip or socket is a compile error, and no sheet ships beside the executable.
/// </summary>
internal static class SheetStep
{
    internal static void Run(BuildPass pass)
    {
        foreach ((Source sheet, Sheet document) in pass.Each(
            pass.Of(AssetType.Sheets),
            source =>
            {
                Sheet sheet = SheetFile.Read(source.Path);

                // Resolved by key against what the build ships, and carrying the shipped extension,
                // however the sheet spelled it.
                if (!pass.Textures.TryGetValue(sheet.TextureKey, out Source texture))
                {
                    throw new FormatException(
                        $"cuts from texture \"{sheet.TextureKey}{sheet.TextureExtension}\", which this game does not ship. Author it at Assets/{sheet.TextureKey}{sheet.TextureExtension}.");
                }

                pass.Progress("sheets", source);

                return sheet with { TextureExtension = texture.Extension };
            }))
        {
            pass.Declare(sheet, document, SheetMembers.Write);
        }
    }
}
