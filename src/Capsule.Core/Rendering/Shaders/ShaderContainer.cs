using System.Buffers.Binary;
using System.Text;

namespace Capsule.Rendering;

// The pipeline stage a compiled shader runs in.
internal enum ShaderStage : byte
{
    Vertex,
    Fragment,
}

// One parameter as a compiled shader binds it: a value at its byte offset in its uniform buffer, the
// scene globals' when Global and else the material's, or a texture at its sampler slot, the sprite's
// being 0.
internal readonly record struct ShaderBinding(string Name, ShaderParameterKind Kind, bool Global, int Location);

// One stage of one shader compiled to one GPU format, as the asset build ships it at
// {name}.{format}.shader: the code, its entry point, the resource counts SDL's GPU API creates a shader
// with, and the parameter table the host binds a material through. The material's uniform buffer is
// slot 0 and the scene globals' slot 1.
internal sealed record ShaderContainer(
    string Format,
    ShaderStage Stage,
    string EntryPoint,
    int Samplers,
    int StorageTextures,
    int StorageBuffers,
    int UniformBuffers,
    ShaderBinding[] Parameters,
    byte[] Code)
{
    // The layout version this file reads and writes. A change to the layout bumps it, and every shader rebuilds.
    internal const byte Version = 1;

    // The extension every container ships under, after its format.
    internal const string Extension = ".shader";

    private static ReadOnlySpan<byte> Magic => "CSHD"u8;

    // The file name suffix of a container in format, as .spirv.shader.
    internal static string Suffix(string format) => "." + format + Extension;

    internal byte[] Write()
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((byte)Stage);
        WriteString(writer, Format);
        WriteString(writer, EntryPoint);
        writer.Write(checked((ushort)Samplers));
        writer.Write(checked((ushort)StorageTextures));
        writer.Write(checked((ushort)StorageBuffers));
        writer.Write(checked((ushort)UniformBuffers));
        writer.Write(checked((ushort)Parameters.Length));
        foreach (ShaderBinding parameter in Parameters)
        {
            WriteString(writer, parameter.Name);
            writer.Write((byte)parameter.Kind);
            writer.Write(parameter.Global);
            writer.Write(checked((ushort)parameter.Location));
        }

        writer.Write(Code.Length);
        writer.Write(Code);
        writer.Flush();

        return stream.ToArray();
    }

    // Reads a container Write wrote. A refusal names the file as name.
    internal static ShaderContainer Read(ReadOnlySpan<byte> bytes, string name)
    {
        if (bytes.Length < 6 || !bytes[..4].SequenceEqual(Magic) || bytes[4] != Version)
        {
            throw new InvalidDataException($"Shader '{name}' is not a version {Version} Capsule shader. Rebuild the game so the shipped shader and the engine agree.");
        }

        ShaderStage stage = (ShaderStage)bytes[5];
        int at = 6;
        string format = ReadString(bytes, ref at);
        string entryPoint = ReadString(bytes, ref at);
        int samplers = ReadUInt16(bytes, ref at);
        int storageTextures = ReadUInt16(bytes, ref at);
        int storageBuffers = ReadUInt16(bytes, ref at);
        int uniformBuffers = ReadUInt16(bytes, ref at);

        ShaderBinding[] parameters = new ShaderBinding[ReadUInt16(bytes, ref at)];
        for (int i = 0; i < parameters.Length; i++)
        {
            string parameter = ReadString(bytes, ref at);
            ShaderParameterKind kind = (ShaderParameterKind)bytes[at];
            bool global = bytes[at + 1] != 0;
            at += 2;
            parameters[i] = new ShaderBinding(parameter, kind, global, ReadUInt16(bytes, ref at));
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes[at..]);
        at += sizeof(int);

        return new ShaderContainer(format, stage, entryPoint, samplers, storageTextures, storageBuffers, uniformBuffers, parameters, bytes.Slice(at, length).ToArray());
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
    }

    private static string ReadString(ReadOnlySpan<byte> bytes, ref int at)
    {
        int length = ReadUInt16(bytes, ref at);
        string value = Encoding.UTF8.GetString(bytes.Slice(at, length));
        at += length;

        return value;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, ref int at)
    {
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(bytes[at..]);
        at += sizeof(ushort);

        return value;
    }
}
