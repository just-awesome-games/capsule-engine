using System.Numerics;

namespace Capsule.Rendering;

/// <summary>
/// One run of a frame's world list drawn by a scroll factor: from <see cref="FirstSprite"/> and
/// <see cref="FirstLine"/> up to the next layer's, or the end of the lists. The intent in the run
/// is at authored world positions; a renderer draws it as if by the frame's camera with its
/// top-left corner moved to <c>ScrollOrigin + (Corner - ScrollOrigin) * ScrollFactor</c>, the
/// corner being that of the world rect the frame places — interpolated and confined exactly as
/// the camera itself is.
/// </summary>
/// <param name="FirstSprite">The index into <see cref="FrameView.Sprites"/> the run starts at.</param>
/// <param name="FirstLine">The index into <see cref="FrameView.Lines"/> the run starts at.</param>
/// <param name="ScrollFactor">The factor the run's entity carries; one on an axis moves with the world.</param>
public readonly record struct ParallaxLayer(int FirstSprite, int FirstLine, Vector2 ScrollFactor);
