namespace Capsule.Rendering;

// The kinds of thing a frame can carry. One member per typed pool on FrameView.
internal enum RenderKind
{
    Sprite,
}

// One entry of a frame's ordered stream: which pool holds the intent, and where in it. The stream
// is what fixes draw order across kinds; a pool only has to keep its own entries addressable.
internal readonly record struct RenderCommand(RenderKind Kind, int Index);
