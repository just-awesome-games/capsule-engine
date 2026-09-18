using System.Numerics;
using System.Text;
using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.UI;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>About 10 000 glyphs of word-wrapped text on the screen layer, static but laid out again every frame as a head-up display is: text layout and glyph submission.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class Text10k : Scene
{
    private const float GlyphScale = 0.4f;

    private static readonly string[] Words =
    [
        "capsule", "draws", "every", "frame", "from", "intent", "the", "simulation", "wrote",
        "kerning", "and", "wrapping", "cost", "layout", "not", "submission", "so", "this", "row",
        "isolates", "both", "at", "once", "for", "a", "head-up", "display", "rewritten", "each", "step",
    ];

    public Text10k()
    {
        Camera = new ParkedCamera();

        // Eight paragraphs of seven lines at 200 columns fill the canvas at this scale.
        for (int index = 0; index < 8; index++)
        {
            Add(new Caption(Anchor.TopLeft, new Vector2(0f, index * 7f * 16f * GlyphScale), Prose(index), wrapWidth: 200f * 8f) { Scale = new Vector2(GlyphScale) });
        }
    }

    private static string Prose(int seed)
    {
        StringBuilder text = new();
        for (int word = 0; word < 190; word++)
        {
            text.Append(word > 0 ? " " : string.Empty).Append(Words[((word * 7) + seed) % Words.Length]);
        }

        return text.ToString();
    }
}
