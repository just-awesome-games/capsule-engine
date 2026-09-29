using Capsule.Build.Configuration;

namespace Capsule.Build.Atlases;

/// <summary>Reads every <c>&lt;name&gt;.atlas.json</c> and sets <see cref="PipelinePass.Atlases"/>, every declared atlas by its name.</summary>
internal static class AtlasDeclarationStep
{
    internal static void Run(PipelinePass pass)
    {
        // A defective file still declares its atlas with a null config. A texture naming it then
        // reads as declared, and the file reports only its own defect.
        Dictionary<string, DeclaredAtlas> atlases = new(StringComparer.Ordinal);
        foreach (Source file in pass.Of(AssetType.Atlases))
        {
            string name = file.Key[(file.Key.LastIndexOf('/') + 1)..];
            if (atlases.TryGetValue(name, out DeclaredAtlas first))
            {
                pass.Fail(file.Path, $"declares the atlas \"{name}\", which '{first.Path}' already declares. An atlas is named by its file name, so rename or delete one.");
                continue;
            }

            AtlasConfigJson? config;
            try
            {
                config = AssetConfigJsonContext.Read(file, AssetConfigJsonContext.Default.AtlasConfigJson, AtlasConfigJson.Shape);
                if (config.MaxSize is { } maxSize && (maxSize <= 0 || maxSize > AtlasConfigJson.LargestMaxSize || !int.IsPow2(maxSize)))
                {
                    throw new FormatException($"sets \"maxSize\" to {maxSize}. {AtlasConfigJson.Shape}");
                }
            }
            catch (Exception ex) when (PipelinePass.IsReportable(ex))
            {
                pass.Fail(file.Path, ex.Message);
                config = null;
            }

            atlases.Add(name, new DeclaredAtlas(file.Path, config));
        }

        pass.Atlases = atlases;
    }
}
