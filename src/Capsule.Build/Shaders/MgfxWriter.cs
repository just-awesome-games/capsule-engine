using System.Security.Cryptography;
using System.Text;
using Capsule.Rendering;

namespace Capsule.Build.Shaders;

/// <summary>One parameter the pixel stage reads from its constant buffer, at its byte offset.</summary>
internal readonly record struct PixelConstant(string Name, ShaderParameterKind Kind, int Offset);

/// <summary>
/// Packs a vertex and a pixel GLSL stage into MonoGame's OpenGL effect container, version 11, as its
/// <c>Effect</c> reader and GL <c>Shader</c> constructor read it: constant buffers, the two shaders
/// with their samplers and attributes, the parameter table and one technique of one pass. Parameters
/// start at zero.
/// </summary>
internal static class MgfxWriter
{
    /// <summary>The container version this writer emits.</summary>
    internal const byte Version = 11;

    // The container's profile byte for OpenGL.
    private const byte OpenGlProfile = 0;

    private const string VertexBuffer = "vs_uniforms_vec4";

    /// <summary>The uniform array the pixel stage's parameters are packed into.</summary>
    internal const string PixelBuffer = "ps_uniforms_vec4";

    // EffectParameterClass and EffectParameterType as the reader casts them.
    private const byte ScalarClass = 0;
    private const byte VectorClass = 1;
    private const byte MatrixClass = 2;
    private const byte ObjectClass = 3;
    private const byte SingleType = 3;
    private const byte Texture2DType = 7;

    // VertexElementUsage as the reader casts it.
    private const byte PositionUsage = 0;
    private const byte ColorUsage = 1;
    private const byte TextureCoordinateUsage = 2;

    /// <summary>The GLSL uniform a texture slot's combined sampler is named, slot 0 being the sprite.</summary>
    internal static string SamplerName(int slot) => "ps_s" + slot;

    /// <summary>
    /// The effect for <paramref name="pixelGlsl"/> under the engine's vertex stage. The parameter table
    /// is the transform, the sprite texture, then <paramref name="constants"/> and
    /// <paramref name="textures"/> in their order. Texture <c>i</c> of <paramref name="textures"/> is
    /// bound at slot <c>i + 1</c>.
    /// </summary>
    /// <param name="source">The file name the shaders record, for a driver's compile error.</param>
    /// <param name="pixelGlsl">The pixel stage, reading <see cref="PixelBuffer"/> and <see cref="SamplerName"/>.</param>
    /// <param name="constants">The parameters the pixel stage reads from <see cref="PixelBuffer"/>.</param>
    /// <param name="pixelBufferSize">The pixel buffer's extent in bytes, a multiple of 16.</param>
    /// <param name="textures">The texture parameters beyond the sprite's, in slot order.</param>
    internal static byte[] Write(
        string source,
        string pixelGlsl,
        IReadOnlyList<PixelConstant> constants,
        int pixelBufferSize,
        IReadOnlyList<string> textures)
    {
        using MemoryStream body = new();
        using (BinaryWriter writer = new(body, Encoding.UTF8, leaveOpen: true))
        {
            const int Transform = 0;
            const int Sprite = 1;
            int firstConstant = 2;
            int firstTexture = firstConstant + constants.Count;
            bool pixelBuffer = constants.Count > 0;

            // Constant buffers: the pixel stage's first when it has one, then the vertex stage's.
            writer.Write(pixelBuffer ? 2 : 1);
            if (pixelBuffer)
            {
                writer.Write(PixelBuffer);
                writer.Write((short)pixelBufferSize);
                writer.Write(constants.Count);
                for (int i = 0; i < constants.Count; i++)
                {
                    writer.Write(firstConstant + i);
                    writer.Write((ushort)constants[i].Offset);
                }
            }

            writer.Write(VertexBuffer);
            writer.Write((short)64);
            writer.Write(1);
            writer.Write(Transform);
            writer.Write((ushort)0);

            // Shader 0 is the pixel stage and shader 1 the vertex stage.
            writer.Write(2);

            WriteShader(writer, vertex: false, source, ShaderTemplate.EntryPoint, pixelGlsl);
            writer.Write((byte)(textures.Count + 1));
            WriteSampler(writer, 0, Sprite);
            for (int i = 0; i < textures.Count; i++)
            {
                WriteSampler(writer, i + 1, firstTexture + i);
            }

            writer.Write((byte)(pixelBuffer ? 1 : 0));
            if (pixelBuffer)
            {
                writer.Write((byte)0);
            }

            writer.Write((byte)0);

            WriteShader(writer, vertex: true, source, "CapsuleVertex", VertexGlsl);
            writer.Write((byte)0);
            writer.Write((byte)1);
            writer.Write((byte)(pixelBuffer ? 1 : 0));
            writer.Write((byte)4);
            WriteAttribute(writer, "vs_v0", PositionUsage, 0);
            WriteAttribute(writer, "vs_v1", ColorUsage, 0);
            WriteAttribute(writer, "vs_v2", ColorUsage, 1);
            WriteAttribute(writer, "vs_v3", TextureCoordinateUsage, 0);

            writer.Write(2 + constants.Count + textures.Count);
            WriteParameter(writer, ShaderTemplate.MatrixTransform, MatrixClass, SingleType, 4, 4);
            WriteParameter(writer, ShaderTemplate.SpriteTexture, ObjectClass, Texture2DType, 0, 0);
            foreach (PixelConstant constant in constants)
            {
                byte columns = constant.Kind switch
                {
                    ShaderParameterKind.Float => 1,
                    ShaderParameterKind.Vector2 => 2,
                    ShaderParameterKind.Vector3 => 3,
                    _ => 4,
                };

                WriteParameter(writer, constant.Name, columns == 1 ? ScalarClass : VectorClass, SingleType, 1, columns);
            }

            foreach (string texture in textures)
            {
                WriteParameter(writer, texture, ObjectClass, Texture2DType, 0, 0);
            }

            // One technique of one pass, vertex shader 1 and pixel shader 0, carrying no state.
            writer.Write(1);
            writer.Write("Sprite");
            writer.Write(0);
            writer.Write(1);
            writer.Write(string.Empty);
            writer.Write(0);
            writer.Write(1);
            writer.Write(0);
            writer.Write(false);
            writer.Write(false);
            writer.Write(false);

            writer.Write("MGFX"u8);
        }

        byte[] content = body.ToArray();

        // The device caches effects by this key, so two different effects must never share one.
        int key = BitConverter.ToInt32(SHA256.HashData(content), 0);

        using MemoryStream effect = new();
        using (BinaryWriter writer = new(effect, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("MGFX"u8);
            writer.Write(Version);
            writer.Write(OpenGlProfile);
            writer.Write(key);
            writer.Write(content);
        }

        return effect.ToArray();
    }

    // The engine's vertex stage, the one every sprite shader shares. The transform arrives as four
    // rows the position is dotted with, and posFixup is the device's correction for GL's clip space and
    // for a render target's flipped Y.
    internal const string VertexGlsl = """
        #version 120

        uniform vec4 vs_uniforms_vec4[4];
        uniform vec4 posFixup;
        attribute vec4 vs_v0;
        attribute vec4 vs_v1;
        attribute vec4 vs_v2;
        attribute vec4 vs_v3;
        varying vec4 vTint;
        varying vec4 vFlash;
        varying vec2 vUV;

        void main()
        {
            gl_Position = vec4(dot(vs_v0, vs_uniforms_vec4[0]), dot(vs_v0, vs_uniforms_vec4[1]), dot(vs_v0, vs_uniforms_vec4[2]), dot(vs_v0, vs_uniforms_vec4[3]));
            vTint = vs_v1;
            vFlash = vs_v2;
            vUV = vs_v3.xy;
            gl_Position.y = gl_Position.y * posFixup.y;
            gl_Position.xy += posFixup.zw * gl_Position.ww;
            gl_Position.z = gl_Position.z * 2.0 - gl_Position.w;
        }

        """;

    /// <summary>The varyings the pixel stage reads, by input location.</summary>
    internal static readonly string[] Varyings = ["vTint", "vFlash", "vUV"];

    private static void WriteShader(BinaryWriter writer, bool vertex, string source, string entryPoint, string glsl)
    {
        writer.Write(vertex);
        writer.Write(source);
        writer.Write(entryPoint);
        byte[] code = Encoding.ASCII.GetBytes(glsl);
        writer.Write(code.Length);
        writer.Write(code);
    }

    // A 2D sampler at slot, read through the parameter at index. No state: the host sets the slot's.
    private static void WriteSampler(BinaryWriter writer, int slot, int parameter)
    {
        writer.Write((byte)0);
        writer.Write((byte)slot);
        writer.Write((byte)slot);
        writer.Write(false);
        writer.Write(SamplerName(slot));
        writer.Write((byte)parameter);
    }

    private static void WriteAttribute(BinaryWriter writer, string name, byte usage, byte index)
    {
        writer.Write(name);
        writer.Write(usage);
        writer.Write(index);
        writer.Write((short)0);
    }

    // No semantic, annotations, elements or members. A float-typed parameter carries its initial
    // values, all zero.
    private static void WriteParameter(BinaryWriter writer, string name, byte parameterClass, byte type, byte rows, byte columns)
    {
        writer.Write(parameterClass);
        writer.Write(type);
        writer.Write(name);
        writer.Write(string.Empty);
        writer.Write(0);
        writer.Write(rows);
        writer.Write(columns);
        writer.Write(0);
        writer.Write(0);

        if (type == SingleType)
        {
            for (int i = 0; i < rows * columns; i++)
            {
                writer.Write(0f);
            }
        }
    }
}
