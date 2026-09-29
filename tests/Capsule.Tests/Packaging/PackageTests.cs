using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Capsule.Scenes;

namespace Capsule.Tests.Packaging;

/// <summary>The engine's packages as pack writes them, from the build these tests run against.</summary>
public sealed class PackageTests
{
    private static readonly TimeSpan PackLimit = TimeSpan.FromMinutes(3);

    // The pure modules ship inside the package, and CapsuleReturnPureModules in its project hands them
    // to a referencing project. A module listed as a dependency would name a package that does not exist.
    [Fact]
    public void JagCapsule_DependsOnlyOnTheShaderTools_AndShipsEveryModuleInLib()
    {
        using ZipArchive package = Pack("Capsule", "JAG.Capsule");

        Assert.Equal(["MonoGame.Tool.Dxc", "MonoGame.Tool.Spirvcross"], Dependencies(package, "JAG.Capsule").Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["Capsule.Core.dll", "Capsule.Physics.dll", "Capsule.Scenes.dll", "Capsule.dll"], Libraries(package));
        Assert.DoesNotContain(package.Entries, static entry => entry.Name.StartsWith("Capsule.Build.", StringComparison.Ordinal));
    }

    // Build-time code only: the machinery every project runs stays in JAG.Capsule, which this package
    // pins at its own release.
    [Fact]
    public void JagCapsuleBuild_ShipsOnlyTheBuildInLib_AndPinsJagCapsuleAtItsRelease()
    {
        using ZipArchive package = Pack("Capsule.Build", "JAG.Capsule.Build");

        Dictionary<string, string> dependencies = Dependencies(package, "JAG.Capsule.Build");
        Assert.Equal(["JAG.Capsule", "StbImageSharp"], dependencies.Keys.Order(StringComparer.Ordinal));
        Assert.Equal($"[{Version(package, "JAG.Capsule.Build")}]", dependencies["JAG.Capsule"]);
        Assert.Equal(["Capsule.Build.dll"], Libraries(package));
        Assert.NotNull(package.GetEntry("lib/net10.0/Capsule.Build.xml"));
        Assert.DoesNotContain(package.Entries, static entry => entry.FullName.Split('/')[0] is "build" or "buildTransitive" or "tools" or "analyzers");
    }

    // Each dependency's id and version range, over every target framework group.
    private static Dictionary<string, string> Dependencies(ZipArchive package, string id) =>
        Nuspec(package, id).Descendants()
            .Where(static element => element.Name.LocalName == "dependency")
            .DistinctBy(static element => (string)element.Attribute("id")!)
            .ToDictionary(static element => (string)element.Attribute("id")!, static element => (string)element.Attribute("version")!);

    private static string Version(ZipArchive package, string id) =>
        Nuspec(package, id).Descendants().Single(static element => element.Name.LocalName == "version").Value;

    private static string[] Libraries(ZipArchive package) =>
        [.. package.Entries
            .Where(static entry => entry.FullName.StartsWith("lib/net10.0/", StringComparison.Ordinal) && entry.Name.EndsWith(".dll", StringComparison.Ordinal))
            .Select(static entry => entry.Name)
            .Order(StringComparer.Ordinal)];

    private static XDocument Nuspec(ZipArchive package, string id)
    {
        ZipArchiveEntry manifest = package.GetEntry($"{id}.nuspec")
            ?? throw new InvalidDataException($"The package holds no {id}.nuspec.");
        using Stream stream = manifest.Open();
        return XDocument.Load(stream);
    }

    // Packs the engine checkout's src/<project> without building it, in the configuration these tests
    // were built in. A reusable MSBuild node the pack starts would inherit its output pipes and outlive
    // it, and reading the output to its end would then never return.
    private static ZipArchive Pack(string project, string id)
    {
        string configuration = typeof(Scene).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string output = Directory.CreateTempSubdirectory("capsule-package-").FullName;

        ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in (string[])["pack", Path.Combine(CheckoutRoot(), "src", project, $"{project}.csproj"),
            "--no-build", "--no-restore", "--configuration", configuration, "--output", output, "-nologo", "-nodeReuse:false"])
        {
            start.ArgumentList.Add(argument);
        }

        using Process pack = Process.Start(start)!;
        Task<string> log = pack.StandardOutput.ReadToEndAsync();
        Task<string> errors = pack.StandardError.ReadToEndAsync();
        if (!pack.WaitForExit(PackLimit))
        {
            pack.Kill(entireProcessTree: true);
            Assert.Fail($"dotnet pack of {id} ran past {PackLimit.TotalMinutes} minutes.");
        }

        Assert.True(pack.ExitCode == 0, $"dotnet pack of {id} failed. Build the solution in {configuration} first.{Environment.NewLine}{log.Result}{errors.Result}");

        // Read whole, so the directory goes before the assertions run.
        MemoryStream bytes = new(File.ReadAllBytes(Directory.EnumerateFiles(output, $"{id}.*.nupkg").Single()));
        Directory.Delete(output, recursive: true);
        return new ZipArchive(bytes, ZipArchiveMode.Read);
    }

    // Walks up from the test binaries to the checkout, which holds the solution.
    private static string CheckoutRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Capsule.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No directory above '{AppContext.BaseDirectory}' holds Capsule.slnx. Run the tests from inside the engine checkout.");
    }
}
