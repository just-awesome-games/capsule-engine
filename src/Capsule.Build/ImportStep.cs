using System.Security.Cryptography;
using Capsule.Build.Caching;

namespace Capsule.Build;

/// <summary>
/// Every source an importer claims, imported under <c>imported/</c> and requested in its place. The
/// key pass then reads each output as though it were authored at its path below the asset root. A
/// shipping pass imports no source under a development-only directory and drops it from the requests.
/// </summary>
/// <remarks>
/// An import is a cached derivation of its source and every file it read through its
/// <see cref="AssetImportContext"/>. A reused import keeps the outputs it wrote. An output no import
/// claims this run is deleted.
/// </remarks>
internal static class ImportStep
{
    private const string Step = "import";

    internal static void Run(PipelinePass pass)
    {
        string requestedRoot = BuildRequests.Relative(pass.Imported.Root);
        string[] developmentOnly = pass.Requests.Shipping ? Keys.DevelopmentOnlyDirectories(pass.Requests) : [];
        List<Request> native = [];
        List<(Request Source, IAssetImporter Importer)> claimed = [];
        foreach (Request request in pass.Requests.Sources)
        {
            if (pass.Configuration.ImporterOf(request.Path) is not { } importer)
            {
                native.Add(request);
            }
            else if (!Keys.IsUnder(developmentOnly, Keys.Below(request.Root ?? pass.Requests.AssetRoot, request.Path)))
            {
                claimed.Add((request, importer));
            }
        }

        List<Request> imported = [];
        foreach ((_, Dictionary<string, FileRecordJson> outputs) in pass.Each(
            Step,
            claimed,
            claim => new Derivation(
                claim.Source.Path,
                [claim.Source.Path],
                $"importer={claim.Importer.GetType().FullName}; root={pass.Requests.AssetRoot}; tileSize={pass.Configuration.TileSize}",
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

        pass.Imported.Prune();
        pass.Cache.Hashes.Found(imported);
        pass.Requests = pass.Requests with { Sources = [.. native, .. imported] };
    }

    // Every output by its path below the asset root, with the hash of the bytes written. A reused
    // import hands these on, and nothing reads its outputs again to hash them.
    private static Dictionary<string, FileRecordJson> Import(PipelinePass pass, Request source, IAssetImporter importer, DerivedFiles files)
    {
        AssetImportContext context = new(source.Path, pass.Requests.AssetRoot, pass.Configuration.TileSize, files);
        importer.Import(context);

        Dictionary<string, FileRecordJson> outputs = new(StringComparer.Ordinal);
        foreach ((string assetPath, byte[] contents) in context.Outputs)
        {
            (_, long length, DateTime written) = files.Write(assetPath, temporary => File.WriteAllBytes(temporary, contents));
            outputs[assetPath] = new FileRecordJson { Length = length, Written = written, Sha256 = Convert.ToHexStringLower(SHA256.HashData(contents)) };
        }

        return outputs;
    }
}
