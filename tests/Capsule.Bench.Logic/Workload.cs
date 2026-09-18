namespace Capsule.Bench.Logic;

/// <summary>
/// Declares a scene a workload: which lane measures it and, windowed, which surface it boots on.
/// The suite refuses a registered scene without one.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
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
}

public enum Surface
{
    /// <summary>640 by 360, point sampled.</summary>
    Canvas360,

    /// <summary>1920 by 1080, linearly sampled.</summary>
    Hd1080,
}
