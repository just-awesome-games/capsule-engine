using System.Numerics;
using Capsule.Rendering;
using Capsule.UI;

namespace Capsule.Bench.Logic.UI;

public sealed class Caption : ScreenEntity
{
    /// <param name="wrapWidth">A box width in font pixels to word-wrap inside, or zero for one line.</param>
    public Caption(Anchor anchor, Vector2 offset, string text, float wrapWidth = 0f)
        : base(anchor, offset) =>
        Add(new Label(BitmapFont.Default, text) { Size = new Vector2(wrapWidth, 0f), Wrap = wrapWidth > 0f ? TextWrap.Word : TextWrap.None });
}
