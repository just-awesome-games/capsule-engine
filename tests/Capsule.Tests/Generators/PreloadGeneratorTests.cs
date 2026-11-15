using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// What CapsulePreloads.g.cs records for each class: the CapsuleAssets members its own code reads, matched by symbol.
public sealed class PreloadGeneratorTests
{
    private const string Hum = "global::Capsule.Generated.CapsuleAssets.Audio.HumSound";
    private const string Chime = "global::Capsule.Generated.CapsuleAssets.Audio.ChimeSound";
    private const string South = "global::Capsule.Generated.CapsuleAssets.Textures.SouthTexture";

    // Each spelling of one read: a using at file scope, then one inside the class's namespace, then the statements of
    // a method. The build imports Capsule.Generated into the project, and a namespace's using resolves through that
    // import. A global using reaches the generator through the same import scopes as any other. The class also reads
    // a scene key, whose type preloads nothing.
    [Theory]
    [InlineData("", "", "Use(CapsuleAssets.Audio.HumSound);")]
    [InlineData("", "", "Use(global::Capsule.Generated.CapsuleAssets.Audio.HumSound);")]
    [InlineData("using static Capsule.Generated.CapsuleAssets.Audio;", "", "Use(HumSound);")]
    [InlineData("global using static Capsule.Generated.CapsuleAssets.Audio;", "", "Use(HumSound);")]
    [InlineData("using static Capsule.Generated.CapsuleAssets;", "", "Use(Audio.HumSound);")]
    [InlineData("using Sounds = Capsule.Generated.CapsuleAssets.Audio;", "", "Use(Sounds.HumSound);")]
    [InlineData("using Tree = Capsule.Generated.CapsuleAssets;", "", "Use(Tree.Audio.HumSound);")]
    [InlineData("", "using Sounds = CapsuleAssets.Audio;", "Use(Sounds.HumSound);")]
    [InlineData("", "using static CapsuleAssets.Audio;", "Use(HumSound);")]
    [InlineData("", "", "System.Func<AudioClip> read = () => CapsuleAssets.Audio.HumSound; Use(read());")]
    [InlineData("", "", "Use(Local()); static AudioClip Local() => CapsuleAssets.Audio.HumSound;")]
    public void EachSpellingOfARead_RecordsTheMemberItNames(string usings, string scopedUsings, string statements)
    {
        string source = $$"""
            {{usings}}

            {{CapsuleAssetsStandIn.Source}}

            namespace Game
            {
                using Capsule.Audio;
                using Capsule.Scenes;
                {{scopedUsings}}

                public sealed class Avatar(EntitySpawn spawn) : Entity(spawn)
                {
                    private static void Use(AudioClip clip)
                    {
                    }

                    private void Sound()
                    {
                        {{statements}}
                        _ = global::Capsule.Generated.CapsuleAssets.Scenes.HallScene;
                    }
                }
            }
            """;

        Assert.Equal([Hum], Entries(source)["global::Game.Avatar"]);
    }

    [Fact]
    public void AReadInsideNameOf_RecordsNothing()
    {
        string source = $$"""
            {{CapsuleAssetsStandIn.Source}}

            namespace Game
            {
                using Capsule.Scenes;

                public sealed class Avatar(EntitySpawn spawn) : Entity(spawn)
                {
                    private string Name => nameof(CapsuleAssets.Audio.HumSound);

                    private Capsule.Audio.AudioClip Sound => CapsuleAssets.Audio.ChimeSound;
                }
            }
            """;

        Assert.Equal([Chime], Entries(source)["global::Game.Avatar"]);
    }

    // The outer class's scan stops at a nested class, which records its own reads. A class generated code cannot
    // name records nothing and reports nothing: its CollectAssets declares by hand.
    [Fact]
    public void ANestedClass_RecordsItsOwnReadsAndAnUnreachableOneNone()
    {
        string source = $$"""
            {{CapsuleAssetsStandIn.Source}}

            namespace Game
            {
                using Capsule.Scenes;

                public sealed class Avatar(EntitySpawn spawn) : Entity(spawn)
                {
                    private Capsule.Audio.AudioClip Sound => CapsuleAssets.Audio.HumSound;

                    public sealed class Badge : Component
                    {
                        private Capsule.Audio.AudioClip Sound => CapsuleAssets.Audio.ChimeSound;
                    }

                    private sealed class Hidden : Component
                    {
                        private Capsule.Assets.TextureHandle Art => CapsuleAssets.Textures.SouthTexture;
                    }
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile(source);
        Dictionary<string, string[]> entries = Entries(updated);

        Assert.Empty(diagnostics);
        Assert.Equal([Hum], entries["global::Game.Avatar"]);
        Assert.Equal([Chime], entries["global::Game.Avatar.Badge"]);
        Assert.Equal(2, entries.Count);
    }

    // A part with no base list belongs to the class all the same.
    [Fact]
    public void APartialClass_RecordsTheReadsOfEveryPart()
    {
        string source = $$"""
            {{CapsuleAssetsStandIn.Source}}

            namespace Game
            {
                using Capsule.Scenes;

                public partial class Avatar(EntitySpawn spawn) : Entity(spawn)
                {
                    private Capsule.Audio.AudioClip Sound => CapsuleAssets.Audio.HumSound;
                }

                public partial class Avatar
                {
                    private Capsule.Assets.TextureHandle Art => CapsuleAssets.Textures.SouthTexture;
                }
            }
            """;

        Assert.Equal([Hum, South], Entries(source)["global::Game.Avatar"]);
    }

    // Every logic assembly holds a tree of the same name, and a referenced assembly's tree is reached only through an
    // extern alias, which generated code does not carry. Its member is never recorded as this assembly's.
    [Fact]
    public void AReadOfAReferencedAssemblysTree_RecordsNothing()
    {
        string source = $$"""
            extern alias Other;

            {{CapsuleAssetsStandIn.Source}}

            namespace Game
            {
                using Capsule.Audio;
                using Capsule.Scenes;
                using Theirs = Other::Capsule.Generated.CapsuleAssets.Audio;

                public sealed class Avatar(EntitySpawn spawn) : Entity(spawn)
                {
                    private AudioClip Ours => CapsuleAssets.Audio.ChimeSound;

                    private AudioClip Borrowed => Theirs.HumSound;
                }
            }
            """;
        MetadataReference other = MetadataReference
            .CreateFromImage(GeneratorHarness.LogicImage("OtherSpecs", CapsuleAssetsStandIn.Source))
            .WithAliases(["Other"]);

        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Run(
            GeneratorHarness.Created("RegistrySpecs", source, GeneratorHarness.References.Add(other)), logic: true, shell: false);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));
        Assert.Equal([Chime], Entries(updated)["global::Game.Avatar"]);
    }

    private static Dictionary<string, string[]> Entries(string source)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) = GeneratorHarness.Compile(source);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(updated.GetDiagnostics()));

        return Entries(updated);
    }

    // Each class's typeof name and the reads its entry adds, from the generated file's own lines.
    private static Dictionary<string, string[]> Entries(Compilation compiled)
    {
        const string Register = "RegisterClassAssets(typeof(";
        const string Add = "assets.Add(";

        Dictionary<string, string[]> entries = [];
        string? type = null;
        List<string> reads = [];
        foreach (string line in GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsulePreloadsFile).Split('\n'))
        {
            int register = line.IndexOf(Register, StringComparison.Ordinal);
            int add = line.IndexOf(Add, StringComparison.Ordinal);
            if (register >= 0)
            {
                type = line[(register + Register.Length)..line.IndexOf(')', register)];
                reads = [];
            }
            else if (add >= 0)
            {
                reads.Add(line[(add + Add.Length)..line.LastIndexOf(')')]);
            }
            else if (line.Trim() == "});" && type is not null)
            {
                entries.Add(type, [.. reads]);
                type = null;
            }
        }

        return entries;
    }
}
