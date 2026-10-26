using System.Buffers.Binary;
using System.Text;

namespace Capsule.Build.Shaders;

/// <summary>
/// Places a SPIR-V module's resources in SDL's descriptor layout by rewriting each named resource's
/// set and binding decorations in place.
/// </summary>
/// <remarks>
/// The compiler numbers a resource the source leaves unbound in set 0. Only the decorations' values
/// change. The module is otherwise the one the compiler validated.
/// </remarks>
internal static class SpirvBindings
{
    private const int HeaderWords = 5;
    private const ushort OpName = 5;
    private const ushort OpDecorate = 71;
    private const uint Binding = 33;
    private const uint DescriptorSet = 34;

    /// <summary>
    /// Rewrites <paramref name="spirv"/> so every resource in <paramref name="places"/> sits at its set
    /// and binding, and returns the name of a resource the compiler bound that it does not place, or null.
    /// </summary>
    internal static string? Place(byte[] spirv, IReadOnlyDictionary<string, (int Set, int Binding)> places)
    {
        Span<byte> module = spirv;
        Dictionary<uint, string> names = [];
        for (int at = HeaderWords; at < module.Length / 4;)
        {
            (ushort opcode, int count) = Instruction(module, at);
            if (opcode == OpName)
            {
                ReadOnlySpan<byte> literal = module.Slice((at + 2) * 4, (count - 2) * 4);
                names[Word(module, at + 1)] = Encoding.UTF8.GetString(literal[..literal.IndexOf((byte)0)]);
            }

            at += count;
        }

        for (int at = HeaderWords; at < module.Length / 4;)
        {
            (ushort opcode, int count) = Instruction(module, at);
            if (opcode == OpDecorate && Word(module, at + 2) is DescriptorSet or Binding)
            {
                string name = names.GetValueOrDefault(Word(module, at + 1), "?");
                if (!places.TryGetValue(name, out (int Set, int Binding) place))
                {
                    return name;
                }

                int value = Word(module, at + 2) == DescriptorSet ? place.Set : place.Binding;
                BinaryPrimitives.WriteUInt32LittleEndian(module[((at + 3) * 4)..], (uint)value);
            }

            at += count;
        }

        return null;
    }

    private static (ushort Opcode, int Count) Instruction(ReadOnlySpan<byte> module, int at)
    {
        uint word = Word(module, at);

        return ((ushort)word, (int)(word >> 16));
    }

    private static uint Word(ReadOnlySpan<byte> module, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(module[(at * 4)..]);
}
