using System.Security.Cryptography;
using Capsule.Build.Caching;
using Capsule.Build.Fonts;

namespace Capsule.Build;

/// <summary>
/// Every source an importer claims, imported under <c>imported/</c> and requested in its place.
/// </summary>
/// <remarks>
/// The key pass then reads each output as though it were authored at its path below the asset root. A
/// shipping pass imports no source under a development-only directory and drops it from the requests.
/// An import is a cached derivation of its source and every file it read through its
/// <see cref="AssetImportContext"/>. A reused import keeps the outputs it wrote. An output no import
/// claims this run is deleted.
/// </remarks>
internal static class ImportStep
{
    private const string Step = "import";

    internal static void Run(PipelinePass pass)
    {
        List<Request> native = [];
        List<(Request Source, IAssetImporter Importer)> claimed = [];
        List<Request> fonts = [];
        foreach (Request request in pass.Requests.Sources)
        {
            bool font = request.Path.EndsWith(AssetType.BakedFont, StringComparison.OrdinalIgnoreCase);
            IAssetImporter? importer = font ? null : pass.Configuration.ImporterOf(request.Path);
            if (!font && importer is null)
            {
                native.Add(request);
            }
            else if (!Keys.IsUnder(pass.DevelopmentOnly, Keys.Below(request.Root ?? pass.Requests.AssetRoot, request.Path)))
            {
                if (font)
                {
                    fonts.Add(request);
                }
                else
                {
                    claimed.Add((request, importer!));
                }
            }
        }

        // Fonts bake last, from every string table the strings step reads, imported ones too.
        List<Request> imported = Imported(pass, claimed);
        if (fonts.Count > 0)
        {
            FontBaker baker = new(FontBaker.TableCodepoints(
                [.. native.Concat(imported)
                    .Where(request => AssetType.Strings.Extension(request.Path) is not null
                        && !Keys.IsUnder(pass.DevelopmentOnly, Keys.Below(request.Root ?? pass.Requests.AssetRoot, request.Path)))
                    .Select(static request => request.Path)]));
            imported.AddRange(Imported(pass, [.. fonts.Select(font => (font, (IAssetImporter)baker))]));
        }

        pass.Imported.Prune();
        pass.Cache.Hashes.Found(imported);
        pass.Requests = pass.Requests with { Sources = [.. native, .. imported] };
    }

    // Imports each claim, or reuses its last import, and requests every output in its place.
    private static List<Request> Imported(PipelinePass pass, List<(Request Source, IAssetImporter Importer)> claimed)
    {
        string requestedRoot = BuildRequests.Relative(pass.Imported.Root);
        List<Request> imported = [];
        foreach ((_, Dictionary<string, FileRecordJson> outputs) in pass.Each(
            Step,
            claimed,
            claim => new Derivation(
                claim.Source.Path,
                [claim.Source.Path],
                $"importer={claim.Importer.GetType().FullName}; root={pass.Requests.AssetRoot}; tileSize={pass.Configuration.TileSize}; text={(claim.Importer as FontBaker)?.Stamp}",
                claim.Importer.GetType().Assembly),
            (claim, files) => Import(pass, claim.Source, claim.Importer, files),
            DerivationCacheJsonContext.Default.DictionaryStringFileRecordJson,
            pass.Imported))
        {
            foreach ((string assetPath, FileRecordJson output) in outputs)
            {
                imported.Add(new Request(
                    BuildRequests.Relative(Path.Combine(pass.Imported.Root, assetPath)),
                    output.Length,
                    output.Written,
                    requestedRoot,
                    output.Sha256));
            }
        }

        return imported;
    }

    // Every output by its path below the asset root, with the hash of the bytes written. A reused
    // import hands these on, and nothing reads its outputs again to hash them.
    private static Dictionary<string, FileRecordJson> Import(PipelinePass pass, Request source, IAssetImporter importer, DerivedFiles files)
    {
        AssetImportContext context = new(source.Path, pass.Requests.AssetRoot) { TileSize = pass.Configuration.TileSize };
        importer.Import(context);
        foreach (string input in context.Inputs)
        {
            files.Reads(input);
        }

        Dictionary<string, FileRecordJson> outputs = new(StringComparer.Ordinal);
        foreach ((string assetPath, byte[] contents) in context.Outputs)
        {
            (_, long length, DateTime written) = files.Write(assetPath, temporary => File.WriteAllBytes(temporary, contents));
            outputs[assetPath] = new FileRecordJson { Length = length, Written = written, Sha256 = Convert.ToHexStringLower(SHA256.HashData(contents)) };
        }

        return outputs;
    }
}
