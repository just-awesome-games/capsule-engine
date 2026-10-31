using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Capsule.Tiles;

namespace Capsule.Build.Scenes;

/// <summary>
/// The entry types whose cells the scene step compiles: the engine's <c>tile-map</c> and every key the game's logic
/// assembly records for a class deriving from <c>TileMap</c>.
/// </summary>
/// <remarks>
/// Only the compiled assembly knows its classes. Once a compile writes a new one, the targets run the build again
/// with <c>--tile-maps</c>, which builds only when its keys differ from those the scene step last built every
/// document with, kept in <see cref="FileName"/>. Every run before the next compile builds with the kept keys.
/// </remarks>
internal static class TileMapKeys
{
    /// <summary>The keys the scene step last built every document with, one per line, under the output directory.</summary>
    internal const string FileName = "tile-maps.txt";

    // The attribute the generator records each key with, declared in the logic assembly itself.
    private const string AttributeNamespace = "Capsule.Generated";
    private const string AttributeName = "CapsuleGeneratedTileMapAttribute";

    /// <summary>The keys the scene step last built every document with, or the engine's alone before it has.</summary>
    internal static string[] Built(string outputDirectory)
    {
        string path = Path.Combine(outputDirectory, FileName);

        return File.Exists(path) ? File.ReadAllLines(path) : [TileMap.Key];
    }

    /// <summary>Keeps the keys the scene step built every document with. The scene step calls it holding the output directory.</summary>
    internal static void KeepBuilt(string outputDirectory, string[] keys) =>
        AtomicFile.WriteText(Path.Combine(outputDirectory, FileName), string.Join('\n', keys));

    /// <summary>
    /// The keys the logic assembly at <paramref name="assemblyPath"/> records, or null when the documents were already
    /// built with them or the assembly cannot be read, with the run's <paramref name="exitCode"/>.
    /// </summary>
    internal static string[]? Changed(string outputDirectory, string assemblyPath, TextWriter error, out int exitCode)
    {
        exitCode = 0;
        try
        {
            string[] keys = Of(assemblyPath);
            string path = Path.Combine(outputDirectory, FileName);

            return File.Exists(path) && keys.AsSpan().SequenceEqual(File.ReadAllLines(path)) ? null : keys;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            error.WriteLine($"{BuildRequests.Relative(assemblyPath)}: cannot be read for its tile-map keys: {ex.Message}");
            exitCode = 1;

            return null;
        }
    }

    // Every key the assembly's tile-map attributes name, the engine's among them, sorted.
    private static string[] Of(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader image = new(stream);
        MetadataReader metadata = image.GetMetadataReader();
        SortedSet<string> keys = new(StringComparer.Ordinal) { TileMap.Key };
        foreach (CustomAttributeHandle handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MethodDefinition)
            {
                continue;
            }

            TypeDefinition type = metadata.GetTypeDefinition(metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType());
            if (metadata.StringComparer.Equals(type.Name, AttributeName) && metadata.StringComparer.Equals(type.Namespace, AttributeNamespace))
            {
                // A custom attribute's value opens with the prolog 0x0001, then its one string argument.
                BlobReader value = metadata.GetBlobReader(attribute.Value);
                value.ReadUInt16();
                keys.Add(value.ReadSerializedString() ?? throw new BadImageFormatException($"{AttributeName} names no key. Rebuild the logic assembly."));
            }
        }

        return [.. keys];
    }
}
