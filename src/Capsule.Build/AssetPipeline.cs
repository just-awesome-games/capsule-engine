using Capsule.Build.Atlases;
using Capsule.Build.Audio;
using Capsule.Build.Caching;
using Capsule.Build.Fonts;
using Capsule.Build.Registry;
using Capsule.Build.Scenes;
using Capsule.Build.Shaders;
using Capsule.Build.Sheets;
using Capsule.Build.Textures;

namespace Capsule.Build;

/// <summary>
/// One run over the asset tree. It walks and keys every source, derives each type, and writes two
/// things under the output directory: <c>assets/</c> holding exactly what the game ships, laid out as
/// the game authored it, and <c>CapsuleAssets.g.cs</c> declaring every asset. A run reports every
/// authoring defect instead of stopping at the first. A run holds the output directory for its whole
/// duration. A second run over the same directory waits for it. Every build runs it, and its
/// <see cref="DerivationCache"/> decides what runs again.
/// </summary>
internal static class AssetPipeline
{
    private const string Step = "capsule";

    /// <summary>
    /// Held open exclusively by the run building the directory. The operating system releases it when
    /// that run exits, however it exits.
    /// </summary>
    internal const string LockFile = ".lock";

    /// <summary>How long a run waits for another run over the same directory before failing.</summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromMinutes(5);

    /// <param name="output">Progress, one line per derivation run and one count per step.</param>
    /// <param name="error">Defects, each anchored to the file that has it.</param>
    /// <returns>0 when the run reported no defect, 1 when it reported any.</returns>
    internal static int Run(
        string assetRoot,
        ShaderTools shaderTools,
        bool shipping,
        string outputDirectory,
        CapsuleBuild configuration,
        TextWriter output,
        TextWriter error)
    {
        PipelinePass pass = new(outputDirectory, configuration, output, error);
        FileStream? held = null;
        DateTime started;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            held = Hold(pass);
            if (held is null)
            {
                return 1;
            }

            started = Start(held);
        }
        catch (Exception ex) when (PipelinePass.IsReportable(ex))
        {
            held?.Dispose();
            pass.Fail(Path.Combine(outputDirectory, LockFile), $"cannot be opened: {ex.Message}");

            return 1;
        }

        using (held)
        {
            return Build(assetRoot, shaderTools, shipping, started, pass);
        }
    }

    // Everything the run does while it holds the output directory.
    private static int Build(string assetRoot, ShaderTools shaderTools, bool shipping, DateTime started, PipelinePass pass)
    {
        string outputDirectory = pass.OutputDirectory;
        try
        {
            pass.Requests = BuildRequests.Walk(assetRoot, shaderTools, shipping);
        }
        catch (Exception ex) when (PipelinePass.IsReportable(ex))
        {
            pass.Fail(assetRoot, $"cannot be read: {ex.Message}");

            return 1;
        }

        try
        {
            pass.Cache = DerivationCache.Read(outputDirectory, started, pass.Requests.Sources, pass.Configuration);
            ImportStep.Run(pass); // claimed sources -> imported/, Requests

            // Unlike the other steps, a refused key ends the run. No step could name that source. The
            // cache is left as the last run wrote it. Every entry still checks its files before reuse.
            int failures = pass.Failures;
            Keys.Derive(pass); // Requests -> Keyed
            if (pass.Failures > failures)
            {
                return 1;
            }

            try
            {
                // A step reports each defective source and the rest still build, so every step runs.
                // Each cached step reuses what the last run derived where its stamp still matches.
                FontStep.Run(pass);             // .fnt sources -> font descriptions, FontPages, font members
                AtlasDeclarationStep.Run(pass); // .atlas.json files -> Atlases
                TextureSettingsStep.Run(pass);  // .config.json files, FontPages, Atlases -> TextureSettings
                AtlasStep.Run(pass);            // TextureSettings, Atlases -> atlas pages, TextureMap
                TextureStep.Run(pass);          // .png sources, TextureSettings -> unpacked textures, TextureMap facts, texture members
                AudioStep.Run(pass);            // .ogg and .wav sources -> shipped copies, sound members
                SheetStep.Run(pass);            // .sheet.json sources, the textures -> sheet members
                ShaderStep.Run(pass);           // .fx sources -> .mgfx effects, shader members
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
            }
            finally
            {
                // Written after every file it vouches for, whatever the run reported.
                pass.Cache.Write();
            }
        }
        catch (Exception ex) when (PipelinePass.IsReportable(ex))
        {
            pass.Fail(outputDirectory, $"cannot be written: {ex.Message}");

            return 1;
        }

        pass.Progress(Step, $"{pass.Keyed.Count} source(s) built into {outputDirectory}");

        return 0;
    }

    // The run's start on the output volume's own clock, read back from a write to the held lock file.
    private static DateTime Start(FileStream held)
    {
        held.Position = 0;
        held.WriteByte(0);
        held.Flush();

        return File.GetLastWriteTimeUtc(held.SafeFileHandle);
    }

    // The lock file opened exclusively, once no other run holds it. Null when another run held it for
    // all of LockWait, reported.
    private static FileStream? Hold(PipelinePass pass)
    {
        string path = Path.Combine(pass.OutputDirectory, LockFile);
        long deadline = Environment.TickCount64 + (long)LockWait.TotalMilliseconds;
        bool waiting = false;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (Environment.TickCount64 < deadline)
            {
                if (!waiting)
                {
                    pass.Progress(Step, $"waiting for another build of {pass.OutputDirectory} to finish");
                    waiting = true;
                }

                Thread.Sleep(100);
            }
            catch (IOException)
            {
                pass.Fail(path, $"is still held by another build of this project after {LockWait.TotalMinutes} minutes. Stop the stuck build, then build again.");

                return null;
            }
        }
    }
}
