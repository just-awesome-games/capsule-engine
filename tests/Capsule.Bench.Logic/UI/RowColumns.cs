using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Bench.Logic.UI;

// A row of eight columns of forty rows. Each row is a caption cell and a bar in a horizontal box, and
// each step widens or narrows the next row's caption cell in turn.
public sealed class RowColumns : BoxContainer
{
    private const int Columns = 8;
    private const int Rows = 40;

    private static readonly Vector2 Narrow = new(24f, 6f);
    private static readonly Vector2 Wide = new(32f, 6f);

    private readonly ScreenEntity[] _captions = new ScreenEntity[Columns * Rows];
    private int _next;

    public RowColumns()
        : base(Axis.Horizontal, Anchor.Center, Vector2.Zero)
    {
        Spacing = 4f;
        Padding = new Insets(4f);
        Add(new ColorRect { Color = new ColorRgba(24, 24, 32) });

        for (int column = 0; column < Columns; column++)
        {
            BoxContainer rows = new(Axis.Vertical, Anchor.Top, Vector2.Zero) { Parent = this, Spacing = 2f };

            for (int row = 0; row < Rows; row++)
            {
                BoxContainer line = new(Axis.Horizontal, Anchor.TopWide, Vector2.Zero) { Parent = rows, Spacing = 2f };
                ScreenEntity caption = new(Anchor.Left, Vector2.Zero) { Parent = line, Size = Narrow };
                caption.Add(new ColorRect { Color = ColorRgba.White });
                ScreenEntity bar = new(Anchor.Left, Vector2.Zero) { Parent = line, Size = new Vector2(30f, 4f) };
                bar.Add(new ColorRect { Color = new ColorRgba(222, 96, 96) });
                _captions[(column * Rows) + row] = caption;
            }
        }
    }

    protected override void OnStep(in StepContext context)
    {
        ScreenEntity caption = _captions[_next];
        caption.Size = caption.Size == Narrow ? Wide : Narrow;
        _next = (_next + 1) % _captions.Length;
    }
}
