using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Runtime.Gpu;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

// The sprite vertex stage on the CPU, for the OpenGL effects, which read four-vertex quads and no
// storage buffer. Each sprite becomes the six vertices SV_VertexID would number, top left, top right,
// bottom left, then top right, bottom right, bottom left, with the corners SpriteQuad.Expand places.
// The diagonal runs top right to bottom left. The other split rasterises its pixels differently.
internal sealed class SpriteVertexStage : ICpuVertexStage
{
    private const string TransformParameter = "MatrixTransform";

    private const string CoverageParameter = "CapsuleCoverage";

    // SpriteUniforms.Coverage's offset.
    private const int CoverageOffset = 96;

    public VertexDeclaration Declaration => SpriteVertex.Declaration;

    public void Run(ReadOnlySpan<byte> storage, ReadOnlySpan<byte> uniforms, int firstVertex, Span<byte> vertices)
    {
        ReadOnlySpan<SpriteInstance> sprites = MemoryMarshal.Cast<byte, SpriteInstance>(storage);
        ref readonly SpriteUniforms frame = ref MemoryMarshal.AsRef<SpriteUniforms>(uniforms);
        Span<SpriteVertex> output = MemoryMarshal.Cast<byte, SpriteVertex>(vertices);
        int first = (int)frame.FirstSprite + (firstVertex / 6);

        for (int at = 0, sprite = first; at < output.Length; at += 6, sprite++)
        {
            ref readonly SpriteInstance instance = ref sprites[sprite];
            SpriteQuad quad = SpriteQuad.Expand(in instance, in frame);
            SpriteVertex topLeft = new(quad.TopLeft, instance, quad.TexTopLeft);
            SpriteVertex topRight = new(quad.TopRight, instance, new Vector2(quad.TexBottomRight.X, quad.TexTopLeft.Y));
            SpriteVertex bottomLeft = new(quad.BottomLeft, instance, new Vector2(quad.TexTopLeft.X, quad.TexBottomRight.Y));
            output[at] = topLeft;
            output[at + 1] = topRight;
            output[at + 2] = bottomLeft;
            output[at + 3] = topRight;
            output[at + 4] = new SpriteVertex(quad.BottomRight, instance, quad.TexBottomRight);
            output[at + 5] = bottomLeft;
        }
    }

    public void SetParameters(Effect effect, ReadOnlySpan<byte> uniforms)
    {
        ref readonly SpriteUniforms frame = ref MemoryMarshal.AsRef<SpriteUniforms>(uniforms);
        Matrix4x4 m = frame.Transform;
        effect.Parameters[TransformParameter].SetValue(new Microsoft.Xna.Framework.Matrix(
            m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44));
        (effect.Parameters[CoverageParameter]
            ?? throw new InvalidOperationException("A shader shipped without the engine's coverage parameter. Rebuild the game so the shipped shader and the engine agree."))
            .SetValue(frame.Coverage);
    }

    public bool SameParameters(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) =>
        a[..64].SequenceEqual(b[..64]) && a.Slice(CoverageOffset, sizeof(float)).SequenceEqual(b.Slice(CoverageOffset, sizeof(float)));

    // 24 bytes of position, tint, flash and texture coordinate, the input the effects read. Position
    // carries no depth. The device fills z with 0 and w with 1 for the shader's float4.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct SpriteVertex(Vector2 position, in SpriteInstance sprite, Vector2 textureCoordinate)
    {
        internal static readonly VertexDeclaration Declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 1),
            new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));

        private readonly Vector2 _position = position;
        private readonly uint _tint = sprite.Tint;
        private readonly uint _flash = sprite.Flash;
        private readonly Vector2 _textureCoordinate = textureCoordinate;
    }
}
