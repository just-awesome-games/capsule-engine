using System.Runtime.InteropServices;

namespace Capsule.Rendering;

/// <summary>
/// Everything a simulation wants drawn. A simulation holds one and rewrites it once per
/// fixed step — <see cref="Clear"/> then <see cref="AddQuad"/> — while the renderer reads
/// it on every draw frame; rewriting anywhere but the step tears. <see cref="Clear"/>
/// retains the quad capacity, so the steady state allocates nothing.
/// </summary>
public sealed class FrameView
{
    private readonly List<QuadIntent> _quads = [];

    /// <summary>The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.</summary>
    public CameraView Camera { get; set; }

    /// <summary>The quads to draw, in the order added. Invalidated by the next mutation.</summary>
    public ReadOnlySpan<QuadIntent> Quads => CollectionsMarshal.AsSpan(_quads);

    /// <summary>Drops every quad, keeping the capacity. <see cref="Camera"/> is left as it stands.</summary>
    public void Clear() => _quads.Clear();

    public void AddQuad(in QuadIntent quad) => _quads.Add(quad);
}
