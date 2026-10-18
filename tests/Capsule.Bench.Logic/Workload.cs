namespace Capsule.Bench.Logic;

/// <summary>
/// Declares a scene a workload: which lane measures it and, windowed, which surface it boots on.
/// The suite refuses a registered scene without one, and measures a scene that declares several
/// lanes once in each.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class WorkloadAttribute(WorkloadKind kind, Surface surface = Surface.Canvas360) : Attribute
{
    public WorkloadKind Kind { get; } = kind;

    public Surface Surface { get; } = surface;

    /// <summary>The row this lane runs and records under, or null for the scene's class name.</summary>
    /// <remarks>A named row is a variant of the scene on the same document, chosen by <see cref="Workloads.Running"/>.</remarks>
    public string? Row { get; set; }
}

public enum WorkloadKind
{
    /// <summary>Headless: an exact number of fixed steps, each timed, with no window, device or media.</summary>
    Simulation,

    /// <summary>Windowed: a fixed real duration of frames with media loaded, measuring submission and frame interval.</summary>
    Rendering,

    /// <summary>Headless: a fixed real duration of frames paced to the step rate, each catching up the steps its time bought, measuring frame interval with no window, device or media.</summary>
    Paced,
}

public enum Surface
{
    /// <summary>640 by 360, point sampled.</summary>
    Canvas360,

    /// <summary>1920 by 1080, linearly sampled.</summary>
    Hd1080,
}
