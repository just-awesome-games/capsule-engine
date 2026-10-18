using Capsule.Bench.Logic.Components;

namespace Capsule.Bench.Logic.UI;

public sealed class Panel : ScreenEntity
{
    public Panel(Anchor anchor, Vector2 offset, Vector2 size)
        : base(anchor, offset) =>
        Add(new NineSlice(SpriteField.Tile, new SliceInsets(8), size));
}
