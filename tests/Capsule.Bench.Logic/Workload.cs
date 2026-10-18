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
}

public enum WorkloadKind
{
    /// <summary>Headless: an exact number of fixed steps, each timed; no window, device or media.</summary>
    Simulation,

    /// <summary>Windowed: a fixed real duration of frames; submission and frame interval, media loaded.</summary>
    Rendering,

    /// <summary>Headless: a fixed real duration of frames paced to the step rate, each catching up the steps its time bought; frame interval, no window, device or media.</summary>
    Paced,
}

public enum Surface
{
    /// <summary>640 by 360, point sampled.</summary>
    Canvas360,

    /// <summary>1920 by 1080, linearly sampled.</summary>
    Hd1080,
}
