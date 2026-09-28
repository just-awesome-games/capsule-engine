namespace Capsule.Build;

/// <summary>
/// Every source an importer claims, imported under <c>imported/</c> and requested in its place. The
/// key pass then reads each output as though it were authored at its path below the asset root. A
/// shipping pass imports no source under a development-only directory and drops it from the requests.
/// </summary>
internal static class ImportStep
{
    private const string Folder = "imported";

    internal static void Run(BuildPass pass)
    {
        // Rewritten whole, so no output of a source since renamed or deleted survives the run.
        string root = Path.Combine(pass.OutputDirectory, Folder);
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        string requestedRoot = BuildRequests.Relative(root);
        List<Request> native = [];
        List<Request> imported = [];
        Dictionary<string, string> importedBy = new(StringComparer.OrdinalIgnoreCase);
        string[] developmentOnly = pass.Requests.Shipping ? Keys.DevelopmentOnlyDirectories(pass.Requests) : [];
        foreach (Request request in pass.Requests.Sources)
        {
            if (pass.Configuration.ImporterOf(request.Path) is not { } importer)
            {
                native.Add(request);
                continue;
            }

            if (Keys.IsUnder(developmentOnly, Keys.Below(request.Root ?? pass.Requests.AssetRoot, request.Path)))
            {
                continue;
            }

            try
            {
                AssetImportContext context = new(request.Path, pass.Requests.AssetRoot, pass.Configuration.TileSize);
                importer.Import(context);
                if (context.Outputs.FirstOrDefault(output => importedBy.ContainsKey(output.AssetPath)) is { AssetPath: { } taken })
                {
                    throw new FormatException(
                        $"imports to \"{taken}\", which '{importedBy[taken]}' already imports to. Rename one of the two sources.");
                }

                foreach ((string assetPath, byte[] contents) in context.Outputs)
                {
                    string path = Path.Combine(root, assetPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, contents);
                    importedBy[assetPath] = request.Path;
                    imported.Add(new Request(BuildRequests.Relative(path), requestedRoot));
                    pass.Progress("import", $"{request.Path} -> {assetPath}");
                }
            }
            catch (Exception ex) when (BuildPass.IsReportable(ex))
            {
                pass.Fail(request.Path, ex.Message);
            }
        }

        pass.Requests = pass.Requests with { Sources = [.. native, .. imported] };
    }
}
