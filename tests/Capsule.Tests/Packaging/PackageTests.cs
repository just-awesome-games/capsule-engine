using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Capsule.Scenes;

namespace Capsule.Tests.Packaging;

/// <summary>JAG.Capsule as pack writes it, from the build these tests run against.</summary>
public sealed class PackageTests
{
    private static readonly TimeSpan PackLimit = TimeSpan.FromMinutes(3);

    // The pure modules ship inside the package, and CapsuleReturnPureModules in its project hands them
    // to a referencing project. A module listed as a dependency would name a package that does not exist.
    [Fact]
    public void JagCapsule_DependsOnlyOnTheShaderTools_AndShipsEveryModuleInLib()
    {
        using ZipArchive package = Pack();

        ZipArchiveEntry manifest = package.GetEntry("JAG.Capsule.nuspec")
            ?? throw new InvalidDataException("The package holds no JAG.Capsule.nuspec.");
        XDocument nuspec;
        using (Stream stream = manifest.Open())
        {
            nuspec = XDocument.Load(stream);
        }

        string[] dependencies = [.. nuspec.Descendants()
            .Where(static element => element.Name.LocalName == "dependency")
            .Select(static element => (string)element.Attribute("id")!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        Assert.Equal(["MonoGame.Tool.Dxc", "MonoGame.Tool.Spirvcross"], dependencies);

        string[] libraries = [.. package.Entries
            .Where(static entry => entry.FullName.StartsWith("lib/net10.0/", StringComparison.Ordinal) && entry.Name.EndsWith(".dll", StringComparison.Ordinal))
            .Select(static entry => entry.Name)
            .Order(StringComparer.Ordinal)];
        Assert.Equal(["Capsule.Core.dll", "Capsule.Physics.dll", "Capsule.Scenes.dll", "Capsule.dll"], libraries);
    }

    // Packs the engine checkout's JAG.Capsule without building it, in the configuration these tests
    // were built in.
    private static ZipArchive Pack()
    {
        string configuration = typeof(Scene).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string output = Directory.CreateTempSubdirectory("capsule-package-").FullName;

        ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in (string[])["pack", Path.Combine(CheckoutRoot(), "src", "Capsule", "Capsule.csproj"),
            "--no-build", "--no-restore", "--configuration", configuration, "--output", output, "-nologo"])
        {
            start.ArgumentList.Add(argument);
        }

        using Process pack = Process.Start(start)!;
        Task<string> log = pack.StandardOutput.ReadToEndAsync();
        Task<string> errors = pack.StandardError.ReadToEndAsync();
        if (!pack.WaitForExit(PackLimit))
        {
            pack.Kill(entireProcessTree: true);
            Assert.Fail($"dotnet pack of JAG.Capsule ran past {PackLimit.TotalMinutes} minutes.");
        }

        Assert.True(pack.ExitCode == 0, $"dotnet pack of JAG.Capsule failed. Build the solution in {configuration} first.{Environment.NewLine}{log.Result}{errors.Result}");

        // Read whole, so the directory goes before the assertions run.
        MemoryStream bytes = new(File.ReadAllBytes(Directory.EnumerateFiles(output, "JAG.Capsule.*.nupkg").Single()));
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
