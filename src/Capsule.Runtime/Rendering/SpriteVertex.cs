using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

// 24 bytes of position, tint, flash and texture coordinate, the input Capsule's sprite shader reads.
// Position carries no depth. The device fills z with 0 and w with 1 for the shader's float4.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SpriteVertex : IVertexType
{
    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 1),
        new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

    public Vector2 Position;

    // The tint, premultiplied as the blend mode packs it.
    public Color Color;

    // The flash colour premultiplied by the tint's straight alpha in RGB, and the flash amount in alpha.
    // It follows Color directly, and the batcher writes the pair as one eight-byte store.
    public Color Flash;

    public Vector2 TextureCoordinate;

    readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}
