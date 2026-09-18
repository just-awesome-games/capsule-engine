using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Capsule.Persistence;

/// <summary>
/// The name of one document in a run's <see cref="SaveStore"/>: settings, a slot, a profile are
/// names, nothing more. A game declares its keys once at its assembly root as
/// <see cref="SaveKey{T}"/> instances over its own <c>JsonSerializerContext</c>.
/// </summary>
public abstract class SaveKey
{
    private protected SaveKey(string name)
    {
        if (!SafeName.IsOneSafeDirectoryName(name))
        {
            throw new ArgumentException(
                "A save name must be a single file name: no separators, no relative segment, no reserved device name, and no trailing dot or space.",
                nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// The document's name, one safe file name, compared exactly. On a file system that folds
    /// case, as Windows and macOS do, two names differing only by case share one file, so a game
    /// declares no such pair.
    /// </summary>
    public string Name { get; }
}

/// <summary>
/// A key that also says what its document holds, through the game context's
/// <see cref="JsonTypeInfo{T}"/>, and optionally what an absent document reads as.
/// </summary>
/// <typeparam name="T">The document's type; any type the game's context serializes.</typeparam>
public sealed class SaveKey<T> : SaveKey
{
    /// <summary>A key with no fallback: reading its absent document throws.</summary>
    /// <param name="name">One safe file name.</param>
    /// <param name="typeInfo">The game context's entry for <typeparamref name="T"/>.</param>
    /// <exception cref="ArgumentException">The name is blank, holds a separator or a character a file name cannot, is <c>.</c> or <c>..</c>, is a reserved device name, or ends in a dot or space.</exception>
    /// <exception cref="ArgumentNullException">The type info is null.</exception>
    public SaveKey(string name, JsonTypeInfo<T> typeInfo)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        TypeInfo = typeInfo;
    }

    /// <summary>
    /// A key whose absent document reads as <paramref name="fallback"/>, serialized here once and
    /// deserialized afresh on each such read, so the instance itself is never handed out.
    /// </summary>
    /// <param name="name">One safe file name.</param>
    /// <param name="typeInfo">The game context's entry for <typeparamref name="T"/>.</param>
    /// <param name="fallback">What an absent document reads as.</param>
    /// <exception cref="ArgumentException">The name is not one safe file name.</exception>
    /// <exception cref="ArgumentNullException">The type info is null.</exception>
    /// <exception cref="JsonException">The fallback cannot be serialized through the type info.</exception>
    public SaveKey(string name, JsonTypeInfo<T> typeInfo, T fallback)
        : this(name, typeInfo)
    {
        FallbackJson = JsonSerializer.Serialize(fallback, typeInfo);
    }

    internal JsonTypeInfo<T> TypeInfo { get; }

    internal string? FallbackJson { get; }
}
