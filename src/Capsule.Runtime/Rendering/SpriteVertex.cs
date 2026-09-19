using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

// Laid out as MonoGame's VertexPositionColorTexture, 24 bytes of position, colour and texture
// coordinate with the same semantics, so the stock SpriteEffect binds it unchanged.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SpriteVertex : IVertexType
{
    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

    public Vector3 Position;

    public Color Color;

    public Vector2 TextureCoordinate;

    readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}
