using System.Numerics;
using System.Runtime.InteropServices;

namespace Capsule.Runtime.Rendering;

[Flags]
internal enum SpriteFlags : uint
{
    None = 0,
    FlipX = 1,
    FlipY = 2,

    // The whole texture, its coordinates zero to one, drawn where it stands: no interpolation, no layer
    // offset and no snap. Size is drawn units per texel, not the drawn extent.
    Whole = 4,
}

// One drawn quad as the sprite vertex stage reads it from the frame's storage buffer: everything a
// SpriteIntent and its texture page say about it, before the frame interpolates and snaps it. Its
// layout is the vertex stage's CapsuleSprite, member for member.
[StructLayout(LayoutKind.Sequential)]
internal struct SpriteInstance
{
    public Vector2 PreviousPosition;

    public Vector2 Position;

    public float PreviousRotation;

    public float Rotation;

    // In region texels from the region's top-left, mirrored across a flipped axis.
    public Vector2 Origin;

    public Vector2 Size;

    // The region's top-left on its page in texels, x in the low half, and its extent the same way.
    public uint Region;

    public uint Extent;

    // The tint as the blend packs it, and the flash colour premultiplied by the tint's alpha with the
    // amount in alpha. Bytes R, G, B, A in order.
    public uint Tint;

    public uint Flash;

    public SpriteFlags Flags;

    private readonly uint _padding;

    internal static uint Pack(int low, int high) => (uint)low | ((uint)high << 16);
}

// The sprite vertex stage's uniforms, its CapsuleVertexUniforms member for member: the transform to
// clip space, the bound texture's texel, the corners a layer is drawn from, the frame's interpolation
// fraction, the pixels per unit a quad snaps to (zero for none), whether the texture is single-channel,
// and the draw's first sprite in the storage buffer.
[StructLayout(LayoutKind.Sequential)]
internal struct SpriteUniforms
{
    public Matrix4x4 Transform;

    public Vector2 TexelSize;

    public Vector2 LayerCorner;

    public Vector2 FrameCorner;

    public float Alpha;

    public float Grid;

    public float Coverage;

    public uint FirstSprite;

    private readonly Vector2 _padding;
}
