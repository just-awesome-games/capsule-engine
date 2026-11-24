using System.Buffers.Binary;
using System.Text;

namespace Capsule.Build.Fonts;

// Whether a face file is one the rasterizer reads without running past its end. The rasterizer trusts
// every offset a face holds, and a file that is no face, or is cut short, would end the build. This checks
// what the rasterizer reads before it looks inside a table: the file's tag, its table directory, the tables
// it draws from, the outlines' index and the character map's subtables. A defect deeper inside a table is
// not checked.
internal static class FaceCheck
{
    private const string NoFace = "is no TrueType or OpenType font, or is cut short. Name a whole .ttf or .otf file.";

    // A TrueType face's version, 1.0 as a 16.16 fixed-point number.
    private const uint TrueTypeVersion = 65536;

    // Each table the rasterizer reads besides the outlines, and the fewest bytes it reads of it.
    private static readonly (string Name, int Least)[] Required = [("cmap", 4), ("head", 54), ("hhea", 36), ("hmtx", 4), ("maxp", 6)];

    /// <summary>What keeps <paramref name="data"/> from being a face the rasterizer reads, as a refusal says it, or null.</summary>
    internal static string? Defect(byte[] data)
    {
        if (data.Length < 12)
        {
            return NoFace;
        }

        string tag = Encoding.Latin1.GetString(data, 0, 4);
        if (tag == "ttcf")
        {
            return "is a font collection. Name one face's .ttf or .otf file.";
        }

        if ((U32(data, 0) != TrueTypeVersion && tag is not ("OTTO" or "true")) || Tables(data) is not { } tables)
        {
            return NoFace;
        }

        if (tables.ContainsKey("CFF2"))
        {
            return "holds CFF2 outlines, which the rasterizer cannot read. Name a face with TrueType or CFF outlines.";
        }

        bool readable = Required.All(required => tables.TryGetValue(required.Name, out (int Offset, int Length) table) && table.Length >= required.Least)
            && HasOutlines(data, tables)
            && CharacterMapFits(data, tables["cmap"]);

        return readable ? null : NoFace;
    }

    // Each table by its tag, or null where the directory or a table runs past the file's end.
    private static Dictionary<string, (int Offset, int Length)>? Tables(byte[] data)
    {
        int count = U16(data, 4);
        if (12 + (count * 16) > data.Length)
        {
            return null;
        }

        Dictionary<string, (int Offset, int Length)> tables = [];
        for (int record = 12; record < 12 + (count * 16); record += 16)
        {
            uint offset = U32(data, record + 8);
            uint length = U32(data, record + 12);
            if ((ulong)offset + length > (ulong)data.Length)
            {
                return null;
            }

            tables[Encoding.Latin1.GetString(data, record, 4)] = ((int)offset, (int)length);
        }

        return tables;
    }

    // TrueType outlines find each glyph through loca: an offset per glyph and one past the last, two bytes
    // each or four as head's indexToLocFormat says. maxp's numGlyphs counts them. CFF outlines open with a
    // 4-byte header.
    private static bool HasOutlines(byte[] data, Dictionary<string, (int Offset, int Length)> tables)
    {
        int glyphs = U16(data, tables["maxp"].Offset + 4);
        int offsetBytes = U16(data, tables["head"].Offset + 50) == 0 ? 2 : 4;

        return (tables.ContainsKey("glyf") && tables.TryGetValue("loca", out (int Offset, int Length) loca) && loca.Length >= (glyphs + 1) * offsetBytes)
            || (tables.TryGetValue("CFF ", out (int Offset, int Length) cff) && cff.Length >= 4);
    }

    // The character map's encoding records, each naming a subtable that starts inside the map.
    private static bool CharacterMapFits(byte[] data, (int Offset, int Length) cmap)
    {
        int encodings = U16(data, cmap.Offset + 2);
        if (4 + (encodings * 8) > cmap.Length)
        {
            return false;
        }

        for (int record = cmap.Offset + 4; record < cmap.Offset + 4 + (encodings * 8); record += 8)
        {
            if ((ulong)U32(data, record + 4) + 4 > (ulong)cmap.Length)
            {
                return false;
            }
        }

        return true;
    }

    private static int U16(byte[] data, int at) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));

    private static uint U32(byte[] data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
}
