using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Capsule.Persistence;

/// <summary>
/// The name of one document in a run's <see cref="SaveStore"/>. A game declares its keys once at
/// its assembly root as <see cref="SaveKey{T}"/> instances over its own
/// <c>JsonSerializerContext</c>.
/// </summary>
public abstract class SaveKey
{
    private protected SaveKey(string name)
    {
        if (!SafeName.IsOneSafeDirectoryName(name))
        {
            throw new ArgumentException(
                "Save name is not a single file name. Remove separators, relative segments, reserved device names, and any trailing dot or space.",
                nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// The document's name, one safe file name, compared ordinally. A file system that folds case maps
    /// two names differing only by case onto one file. A game must not declare such a pair.
    /// </summary>
    public string Name { get; }
}

/// <summary>
/// A key that also says what its document holds, through the game context's
/// <see cref="JsonTypeInfo{T}"/>, and optionally what an absent document reads as.
/// </summary>
/// <typeparam name="T">The document's type, which is any type the game's context serializes.</typeparam>
/// <example>
/// <code>
/// public static readonly SaveKey&lt;GameSettings&gt; Settings =
///     new("settings", GameSaveContext.Default.GameSettings, new GameSettings());
///
/// // From a step, at a save moment:
/// Run.Saves.Write(GameSaves.Settings, settings);
/// GameSettings stored = Run.Saves.Read(GameSaves.Settings);
/// </code>
/// </example>
public sealed class SaveKey<T> : SaveKey
{
    /// <summary>A key with no fallback, so reading its absent document throws.</summary>
    /// <param name="name">One safe file name.</param>
    /// <param name="typeInfo">The game context's entry for <typeparamref name="T"/>.</param>
    /// <exception cref="ArgumentException">The name is not one safe file name.</exception>
    public SaveKey(string name, JsonTypeInfo<T> typeInfo)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        TypeInfo = typeInfo;
    }

    /// <summary>
    /// A key whose absent document reads as <paramref name="fallback"/>. The fallback is serialized
    /// once here and deserialized afresh on each such read, so this instance is never handed out.
    /// </summary>
    /// <param name="name">One safe file name.</param>
    /// <param name="typeInfo">The game context's entry for <typeparamref name="T"/>.</param>
    /// <param name="fallback">What an absent document reads as.</param>
    /// <exception cref="ArgumentException">The name is not one safe file name.</exception>
    /// <exception cref="JsonException">The fallback cannot be serialized through the type info.</exception>
    public SaveKey(string name, JsonTypeInfo<T> typeInfo, T fallback)
        : this(name, typeInfo)
    {
        FallbackJson = JsonSerializer.Serialize(fallback, typeInfo);
    }

    internal JsonTypeInfo<T> TypeInfo { get; }

    internal string? FallbackJson { get; }
}
