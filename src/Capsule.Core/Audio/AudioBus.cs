namespace Capsule.Audio;

/// <summary>
/// A named group voices are mixed and paused through, such as music, sfx or dialogue.
/// </summary>
/// <remarks>
/// A bus registers with a <see cref="AudioMixer"/> the first time that mixer plays on it or changes
/// it, at volume 1 and unpaused, with its parent registered first. Names are compared ordinally, and
/// two buses with one name are one bus. A bus's volume, effects and pause state belong to the run.
/// Each stands across a scene transition until something sets it again. A registered bus is never
/// removed. Declare each bus once, as a <c>static readonly</c> field, below its parent. A name built
/// per play grows the mixer's table without bound. The first mixer call that meets a declaration whose
/// name another bus holds under a different parent, or whose chain repeats a name, throws
/// <see cref="InvalidOperationException"/>, reads included, and changes nothing.
/// <para>
/// A bus runs its own effects and volume, then adds into its parent, which runs its own on the sum.
/// The two volumes multiply, and pausing the parent holds every voice under it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public static readonly AudioBus Music = new("music");
/// public static readonly AudioBus Score = new("score", Music);
/// </code>
/// </example>
public sealed record AudioBus
{
    private const string MasterName = "master";

    /// <summary>The bus every other one adds into, directly or through its parents. Its effects and volume run last, and pausing it pauses every voice.</summary>
    public static AudioBus Master { get; } = new();

    /// <summary>The bus's name.</summary>
    public string Name { get; }

    /// <summary>The bus this one adds into, or null for <see cref="Master"/>.</summary>
    public AudioBus? Parent { get; }

    /// <summary>A bus that adds into <see cref="Master"/>.</summary>
    public AudioBus(string name)
        : this(name, Master)
    {
    }

    /// <summary>A bus that adds into <paramref name="parent"/>.</summary>
    public AudioBus(string name, AudioBus parent)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(parent, nameof(parent));
        if (string.Equals(name, MasterName, StringComparison.Ordinal))
        {
            throw new ArgumentException($"The name '{MasterName}' is AudioBus.Master's. Give the bus another name.", nameof(name));
        }

        Name = name;
        Parent = parent;
    }

    private AudioBus()
    {
        Name = MasterName;
    }
}
