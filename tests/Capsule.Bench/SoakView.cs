namespace Capsule.Bench;

/// <summary>
/// <see cref="Soak"/> that also builds the frame between steps, as a presenting host does, which puts
/// the build's samples into the trace beside the step's.
/// </summary>
public sealed class SoakView : Soak
{
    public SoakView()
        : base(buildsView: true)
    {
    }
}
