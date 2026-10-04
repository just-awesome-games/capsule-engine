namespace Capsule.Rendering;

/// <summary>The named points and rects one frame carries, shared by every sprite drawing that frame.</summary>
/// <remarks>
/// Marks are immutable and compared by instance. A generated sheet builds one per frame. Every read of
/// that frame carries the same marks.
/// </remarks>
public sealed class SpriteMarks
{
    private readonly SpriteSocket[] _sockets;
    private readonly SpriteBox[] _boxes;

    /// <summary>Creates marks from copies of <paramref name="sockets"/> and <paramref name="boxes"/>.</summary>
    /// <param name="sockets">The frame's sockets. Names are unique among them.</param>
    /// <param name="boxes">The frame's boxes, each with a finite area on both axes. Names are unique among them.</param>
    public SpriteMarks(ReadOnlySpan<SpriteSocket> sockets = default, ReadOnlySpan<SpriteBox> boxes = default)
    {
        for (int i = 0; i < sockets.Length; i++)
        {
            Named(sockets[i].Name, i, nameof(sockets));
            for (int j = 0; j < i; j++)
            {
                Unique(sockets[j].Name, sockets[i].Name, nameof(sockets));
            }
        }

        for (int i = 0; i < boxes.Length; i++)
        {
            Named(boxes[i].Name, i, nameof(boxes));
            for (int j = 0; j < i; j++)
            {
                Unique(boxes[j].Name, boxes[i].Name, nameof(boxes));
            }

            if (boxes[i].Area.IsEmpty)
            {
                throw new ArgumentException(
                    $"Box \"{boxes[i].Name}\" has area {boxes[i].Area}. Give each box finite edges with Right past Left and Bottom past Top.",
                    nameof(boxes));
            }
        }

        _sockets = sockets.ToArray();
        _boxes = boxes.ToArray();
    }

    /// <summary>The frame's named points, in <see cref="Sprite.Pivot"/>'s texel space.</summary>
    public ReadOnlySpan<SpriteSocket> Sockets => _sockets;

    /// <summary>The frame's named rects, in <see cref="Sprite.Pivot"/>'s texel space.</summary>
    public ReadOnlySpan<SpriteBox> Boxes => _boxes;

    private static void Named(string name, int index, string parameter)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException($"Entry {index} has no name. Name every socket and box.", parameter);
        }
    }

    private static void Unique(string earlier, string name, string parameter)
    {
        if (string.Equals(earlier, name, StringComparison.Ordinal))
        {
            throw new ArgumentException($"\"{name}\" is named twice. Give each mark of one kind its own name.", parameter);
        }
    }
}
