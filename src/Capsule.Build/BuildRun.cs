using Capsule.Build.Atlases;
using Capsule.Build.Audio;
using Capsule.Build.Fonts;
using Capsule.Build.Registry;
using Capsule.Build.Scenes;
using Capsule.Build.Shaders;
using Capsule.Build.Sheets;
using Capsule.Build.Textures;

namespace Capsule.Build;

/// <summary>
/// One run over one request manifest. It keys every source, derives each type, and writes two things
/// under the output directory: <c>assets/</c> holding exactly what the game ships, laid out as the game
/// authored it, and <c>CapsuleAssets.g.cs</c> declaring every asset. A run reports every authoring
/// defect instead of stopping at the first.
/// </summary>
internal static class BuildRun
{
    /// <summary>
    /// Empty, and written last. It is the run's single MSBuild output. A run that failed part way
    /// leaves it older than the manifest, and the next build runs the pass again.
    /// </summary>
    private const string StampFile = "build.stamp";

    /// <param name="requestsPath">The manifest to run.</param>
    /// <param name="outputDirectory">Where everything is written.</param>
    /// <param name="configuration">What the game's build project configured.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Defects, each anchored to the file that has it.</param>
    /// <returns>0 when the run reported no defect, 1 when it reported any.</returns>
    internal static int Run(string requestsPath, string outputDirectory, CapsuleBuild configuration, TextWriter output, TextWriter error)
    {
        BuildPass pass = new(outputDirectory, configuration, output, error);
        try
        {
            Directory.CreateDirectory(outputDirectory);
            pass.Requests = BuildRequests.Read(requestsPath);
        }
        catch (Exception ex) when (BuildPass.IsReportable(ex))
        {
            pass.Fail(requestsPath, ex.Message);

            return 1;
        }

        try
        {
            ImportStep.Run(pass); // claimed sources -> imported/, Requests

            // Unlike the other steps, a refused key ends the run. No step could name that source.
            int failures = pass.Failures;
            Keys.Derive(pass); // Requests -> Keyed
            if (pass.Failures > failures)
            {
                return 1;
            }

            // A step reports each defective source and the rest still build, so every step runs.
            FontStep.Run(pass);             // .fnt sources -> FontPages, font members
            AtlasDeclarationStep.Run(pass); // .atlas.json files -> Atlases
            TextureSettingsStep.Run(pass);  // .config.json files, FontPages, Atlases -> TextureSettings
            AtlasStep.Run(pass);            // TextureSettings, Atlases -> atlas pages, atlases/ stamps, TextureMap
            TextureStep.Run(pass);          // .png sources, TextureSettings -> unpacked textures, TextureMap facts, texture members
            AudioStep.Run(pass);            // .ogg and .wav sources -> shipped copies, sound members
            SheetStep.Run(pass);            // .sheet.json sources, the textures -> sheet members
            ShaderStep.Run(pass);           // .fx sources -> .mgfx effects, shaders/ sources, shader members
            SceneStep.Run(pass);            // .scene.json sources -> gzipped documents, scene members

            // Shipped whatever failed, as every file the steps shipped already is.
            pass.TextureMap.Ship(pass.Shipped);

            // Rendered whatever failed, so a name C# would refuse is reported beside every other defect.
            string? generated = pass.Assets.Render(pass);
            if (pass.Failures > 0 || generated is null)
            {
                return 1;
            }

            AtomicFile.WriteText(Path.Combine(outputDirectory, CapsuleAssetsFile.FileName), generated);
            pass.Shipped.Prune();
            // Always written, never compared, since its timestamp is what MSBuild reads.
            File.WriteAllText(Path.Combine(outputDirectory, StampFile), string.Empty);
        }
        catch (Exception ex) when (BuildPass.IsReportable(ex))
        {
            pass.Fail(outputDirectory, $"cannot be written: {ex.Message}");

            return 1;
        }

        pass.Progress("capsule", $"{pass.Keyed.Count} source(s) built into {outputDirectory}");

        return 0;
    }
}
