namespace Capsule.Bench;

// The asset corpus's projects, wired as docs/build-and-publish.md wires a game building from an engine
// clone: a logic project holding Assets/, and a build project running CapsuleBuild with a toy importer.
// The corpus has no shell, since building the logic project is what runs the asset build.
internal static class AssetCorpusProjects
{
    // The engine repository's own Directory.Build.props and .targets are for the engine's projects.
    // These shadow them, as a game outside the repository would have its own.
    private const string DirectoryBuildProps = """
        <Project>
          <Import Project="$(MSBuildThisFileDirectory)Directory.Build.local.props" Condition="Exists('$(MSBuildThisFileDirectory)Directory.Build.local.props')" />

          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
        </Project>

        """;

    private const string DirectoryBuildTargets = """
        <Project>
        </Project>

        """;

    private const string LogicProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <CapsuleGameLogic>true</CapsuleGameLogic>
            <CapsuleBuildProject>../AssetCorpus.Build/AssetCorpus.Build.csproj</CapsuleBuildProject>
          </PropertyGroup>

          <!-- Source mode replaces the package with the clone's project before restore. -->
          <ItemGroup>
            <PackageReference Include="JAG.Capsule" Version="[0.0.0]" />
          </ItemGroup>
        </Project>

        """;

    private const string BuildProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="JAG.Capsule.Build" Version="[0.0.0]" />
          </ItemGroup>
        </Project>

        """;

    private const string BuildProgram = """
        using AssetCorpus.Build;
        using Capsule.Build;

        return CapsuleBuild.Configure(args)
            .AddImporter(new RoomImporter())
            .WithTileSize(16)
            .Run();

        """;

    // Reads its palette through the import context, as a map importer reads the tileset a map names.
    private const string RoomImporter = """
        using System.Text;
        using Capsule.Build;

        namespace AssetCorpus.Build;

        // A toy editor format. A room names the palette it draws with and its size, then holds its grid,
        // one comma-separated row per line. It imports as a scene document with one tile map.
        public sealed class RoomImporter : IAssetImporter
        {
            public IReadOnlyList<string> Extensions { get; } = [".room"];

            public void Import(AssetImportContext context)
            {
                string[] room = context.ReadAllText(context.SourcePath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string[] palette = context.ReadAllText(Path.Combine(context.AssetRoot, room[0]["palette ".Length..])).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string[] size = room[1].Split(' ');

                StringBuilder scene = new();
                scene.Append("{\n  \"entities\": [\n    {\n      \"type\": \"tile-map\",\n");
                scene.Append($"      \"tileSize\": 16,\n      \"width\": {size[1]},\n      \"height\": {size[2]},\n      \"texture\": \"{palette[0]["texture ".Length..]}\",\n      \"columns\": {palette[1]["columns ".Length..]},\n");
                scene.Append("      \"tileTypes\": [\n        { \"name\": \"empty\" }");
                foreach (string line in palette.Skip(2))
                {
                    string[] fields = line.Split(' ');
                    string layer = fields.Length > 2 ? $", \"layer\": \"{fields[2]}\"" : string.Empty;
                    scene.Append($",\n        {{ \"name\": \"{fields[0]}\", \"cell\": {fields[1]}{layer} }}");
                }

                scene.Append("\n      ],\n      \"tiles\": [\n");
                foreach (string row in room.Skip(2))
                {
                    scene.Append("        ").Append(row).Append('\n');
                }

                scene.Append("      ]\n    }\n  ]\n}\n");
                context.Write(Path.ChangeExtension(context.AssetPath, ".scene.json"), scene.ToString());
            }
        }

        """;

    // Everything but Assets/, with the engine clone named relative to the corpus root.
    internal static void Write(string root, string engineClone)
    {
        Write(root, "Directory.Build.props", DirectoryBuildProps);
        Write(root, "Directory.Build.targets", DirectoryBuildTargets);
        Write(root, "Directory.Build.local.props", $"""
            <Project>
              <PropertyGroup>
                <CapsuleSourcePath>{engineClone}</CapsuleSourcePath>
              </PropertyGroup>
              <Import Project="$(CapsuleSourcePath)/build/Capsule.Build.props" Condition="'$(CapsuleSourcePath)' != ''" />
            </Project>

            """);
        Write(root, "src/AssetCorpus.Game/AssetCorpus.Game.csproj", LogicProject);
        Write(root, "src/AssetCorpus.Build/AssetCorpus.Build.csproj", BuildProject);
        Write(root, "src/AssetCorpus.Build/Program.cs", BuildProgram);
        Write(root, "src/AssetCorpus.Build/RoomImporter.cs", RoomImporter);
    }

    private static void Write(string root, string relativePath, string text)
    {
        string path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
    }
}
