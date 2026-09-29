using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Capsule.Build.Caching;

/// <summary>
/// Which build of the code implements a derivation: a SHA-256 over the .NET runtime's description and
/// the module version ID of its assembly and of every assembly beside it that it references,
/// transitively. Deterministic compilation keeps each ID while the code is unchanged.
/// </summary>
/// <param name="identify">What identifies an assembly in place of its closure, which a test injects. Null reads the closure.</param>
internal sealed class ToolBuilds(Func<Assembly, string>? identify)
{
    private readonly ConcurrentDictionary<Assembly, string> _known = [];

    /// <summary>The build of <paramref name="assembly"/>, computed once per run and safe to ask for concurrently.</summary>
    internal string Of(Assembly assembly) =>
        _known.GetOrAdd(assembly, static (assembly, identify) => identify?.Invoke(assembly) ?? Closure(assembly), identify);

    private static string Closure(Assembly assembly)
    {
        // The framework's assemblies are not beside the tool, and the runtime's description stands in
        // for them. A runtime update can change what a derivation writes, as zlib's compressed bytes
        // do. The first build after one re-derives everything.
        StringBuilder text = new();
        text.Append(RuntimeInformation.FrameworkDescription).Append('\n');

        // An assembly loaded from no file has only its own ID to go by.
        if (assembly.Location is not { Length: > 0 } location)
        {
            return Hash(text.Append(assembly.ManifestModule.ModuleVersionId.ToString("N")));
        }

        string directory = Path.GetDirectoryName(location)!;
        SortedDictionary<string, Guid> closure = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> pending = new([location]);
        while (pending.TryPop(out string? path))
        {
            using FileStream file = File.OpenRead(path);
            using PEReader image = new(file);
            MetadataReader metadata = image.GetMetadataReader();
            if (!closure.TryAdd(metadata.GetString(metadata.GetAssemblyDefinition().Name), metadata.GetGuid(metadata.GetModuleDefinition().Mvid)))
            {
                continue;
            }

            foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
            {
                string name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
                string referenced = Path.Combine(directory, name + ".dll");
                if (!closure.ContainsKey(name) && File.Exists(referenced))
                {
                    pending.Push(referenced);
                }
            }
        }

        foreach ((string name, Guid id) in closure)
        {
            text.Append(name).Append(' ').Append(id.ToString("N")).Append('\n');
        }

        return Hash(text);
    }

    private static string Hash(StringBuilder text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
}
