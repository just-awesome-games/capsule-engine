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

    // One triangle of a polygon, its three corners at both ends of the step in place of the quad: the first
    // from PreviousPosition to Position, the second from (PreviousRotation, Rotation) to Origin, the third
    // from Size to the float bits of (Region, Extent). It reads the white texel at its centre.
    Triangle = 8,
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

    // How many times the tint's colour is drawn: a light's intensity, and 1 on every other quad.
    public float Intensity;

    internal static uint Pack(int low, int high) => (uint)low | ((uint)high << 16);
}

// The sprite vertex stage's uniforms, its CapsuleVertexUniforms member for member: the transform to
// clip space, the bound texture's texel, the GridPlacement the quads are placed by, whether the texture is
// single-channel, the draw's first sprite in the storage buffer, and the scale and offset from clip space to
// the light's texture coordinates.
[StructLayout(LayoutKind.Sequential)]
internal struct SpriteUniforms
{
    public Matrix4x4 Transform;

    public Vector2 TexelSize;

    public Vector2 PreviousLayerCorner;

    public Vector2 LayerCorner;

    public Vector2 FrameCorner;

    public Vector2 Shift;

    public Vector2 Grids;

    public float Alpha;

    public float Grid;

    public float Coverage;

    public uint FirstSprite;

    public Vector4 LightPlace;
}
