using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Gpu;

// The engine's vertex stage run on the CPU, for OpenGL effects that read vertices and no storage
// buffer. The renderer that owns the vertex stage supplies it.
internal interface ICpuVertexStage
{
    // The vertex the effects read.
    VertexDeclaration Declaration { get; }

    // Writes vertices.Length / Declaration.VertexStride vertices, those the vertex stage would
    // output for SV_VertexID firstVertex onwards, reading storage under uniforms.
    void Run(ReadOnlySpan<byte> storage, ReadOnlySpan<byte> uniforms, int firstVertex, Span<byte> vertices);

    // Sets the effect's own vertex-stage parameters from uniforms.
    void SetParameters(Effect effect, ReadOnlySpan<byte> uniforms);

    // Whether two pushes of the uniforms set the effect's parameters alike.
    bool SameParameters(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b);
}
