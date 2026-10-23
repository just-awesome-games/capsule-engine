using System.Text;
using Capsule.Build.Audio;

namespace Capsule.Build.Registry;

/// <summary>
/// The generated <c>CapsuleAssetIndex</c> the generator's lookups search: every texture, sound and scene
/// document <c>CapsuleAssets</c> declares, by the key a scene document names it by.
/// </summary>
/// <remarks>
/// The index is constant data a binary search reads, with no code per asset. A string switch over
/// thousands of keys takes the compiler minutes, and a JIT-compiled run pauses on its first call.
/// </remarks>
internal sealed class AssetIndex
{
    /// <summary>The generated class's name in <c>Capsule.Generated</c>.</summary>
    internal const string ClassName = "CapsuleAssetIndex";

    private readonly List<string> _textures = [];

    private readonly List<(string Path, AudioProbe.Measurement Measured)> _sounds = [];

    private readonly List<string> _scenes = [];

    /// <summary>Indexes <paramref name="texture"/> by its key and extension.</summary>
    internal void Texture(Source texture) => _textures.Add(texture.Key + texture.Extension);

    /// <summary>Indexes <paramref name="sound"/> by its key and extension, with what the build measured of it.</summary>
    internal void Sound(Source sound, AudioProbe.Measurement measured) => _sounds.Add((sound.Key + sound.Extension, measured));

    /// <summary>Indexes <paramref name="document"/> by its key.</summary>
    internal void Scene(Source document) => _scenes.Add(document.Key);

    /// <summary>The class's C# text, the same on every machine whatever order the assets were indexed in.</summary>
    internal string Render()
    {
        // Keys sort by their UTF-8 bytes, the order the search compares them in.
        string[] textures = [.. Sorted(_textures, static path => path)];
        (string Path, AudioProbe.Measurement Measured)[] sounds = [.. Sorted(_sounds, static sound => sound.Path)];
        string[] scenes = [.. Sorted(_scenes, static key => key)];

        StringBuilder data = new();
        Keys(data, "Texture", textures);
        Keys(data, "Sound", sounds.Select(static sound => sound.Path));
        Doubles(data, "SoundDurations", sounds.Select(static sound => sound.Measured.DurationSeconds));
        Doubles(data, "SoundLoops", sounds.SelectMany(static sound => new[] { sound.Measured.Loop.StartSeconds, sound.Measured.Loop.EndSeconds }));
        Keys(data, "Scene", scenes);

        return $$"""
                /// <summary>Every texture, sound and scene document this game ships, by the key a scene document names it by. Generated code.</summary>
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                internal static class {{ClassName}}
                {
                    /// <summary>The texture <paramref name="path"/> names by its key and extension, or null for one the game does not ship.</summary>
                    internal static {{GeneratedTypes.TextureHandle}}? FindTexture(string path)
                    {
                        if (Find(TextureKeys, TextureEnds, path) < 0)
                        {
                            return null;
                        }

                        int dot = path.LastIndexOf('.');

                        return new {{GeneratedTypes.TextureHandle}}(path[..dot], path[dot..]);
                    }

                    /// <summary>The sound <paramref name="path"/> names by its key and extension, or null for one the game does not ship.</summary>
                    internal static {{GeneratedTypes.AudioClip}}? FindSound(string path)
                    {
                        int index = Find(SoundKeys, SoundEnds, path);
                        if (index < 0)
                        {
                            return null;
                        }

                        int dot = path.LastIndexOf('.');

                        return new {{GeneratedTypes.AudioClip}}(
                            path[..dot],
                            path[dot..],
                            SoundDurations[index],
                            new {{GeneratedTypes.AudioLoopRegion}}(SoundLoops[index * 2], SoundLoops[(index * 2) + 1]));
                    }

                    /// <summary>The scene document <paramref name="key"/> names, or null for one the game does not ship.</summary>
                    internal static {{GeneratedTypes.SceneKey}}? FindScene(string key) =>
                        Find(SceneKeys, SceneEnds, key) < 0 ? null : new {{GeneratedTypes.SceneKey}}(key);

                    // The index of key among the sorted keys packed end to end, each ending where ends says, or -1. A
                    // key too long for the stack allocates its bytes.
                    private static int Find(global::System.ReadOnlySpan<byte> keys, global::System.ReadOnlySpan<int> ends, string key)
                    {
                        const int StackBytes = 256;
                        int length = global::System.Text.Encoding.UTF8.GetByteCount(key);
                        global::System.Span<byte> query = length <= StackBytes ? stackalloc byte[StackBytes] : new byte[length];
                        query = query[..global::System.Text.Encoding.UTF8.GetBytes(key, query)];

                        int low = 0;
                        int high = ends.Length - 1;
                        while (low <= high)
                        {
                            int middle = low + ((high - low) / 2);
                            int start = middle == 0 ? 0 : ends[middle - 1];
                            int order = global::System.MemoryExtensions.SequenceCompareTo(keys[start..ends[middle]], query);
                            if (order == 0)
                            {
                                return middle;
                            }

                            if (order < 0)
                            {
                                low = middle + 1;
                            }
                            else
                            {
                                high = middle - 1;
                            }
                        }

                        return -1;
                    }

                    // Every key and measurement is a span over constant data, read from the assembly with no allocation.
            {{data}}    }

            """;
    }

    // One asset type's keys as UTF-8 bytes end to end, and each key's exclusive end in them.
    private static void Keys(StringBuilder data, string type, IEnumerable<string> keys)
    {
        List<int> ends = [];
        int end = 0;
        foreach (string key in keys)
        {
            end += Encoding.UTF8.GetByteCount(key);
            ends.Add(end);
        }

        data.AppendLine().Append("        private static global::System.ReadOnlySpan<byte> ").Append(type).Append("Keys => ")
            .Append(Literal.Of(string.Concat(keys))).AppendLine("u8;");
        data.AppendLine().Append("        private static global::System.ReadOnlySpan<int> ").Append(type).Append("Ends => [")
            .AppendJoin(", ", ends.Select(Literal.Of)).AppendLine("];");
    }

    private static void Doubles(StringBuilder data, string name, IEnumerable<double> values) =>
        data.AppendLine().Append("        private static global::System.ReadOnlySpan<double> ").Append(name).Append(" => [")
            .AppendJoin(", ", values.Select(Literal.Of)).AppendLine("];");

    // Ordinal by UTF-8 bytes, which differs from ordinal by UTF-16 units only past a surrogate. Each key is
    // encoded once, since the build renders the index on every run.
    private static IEnumerable<T> Sorted<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.OrderBy(item => Encoding.UTF8.GetBytes(key(item)), ByteOrder.Instance);

    private sealed class ByteOrder : IComparer<byte[]>
    {
        internal static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
