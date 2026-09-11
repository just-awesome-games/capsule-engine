using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Input;
using Capsule.Scenes.Rendering;

namespace MinimalGame.Game.Scenes;

/// <summary>
/// The boot scene, and the one backed by no <c>*.scene.json</c>. Its public parameterless constructor
/// is what marks it class-only: <c>RunScene&lt;MainMenu&gt;()</c> builds it as it is, with no document
/// composed into it.
/// <para>
/// Everything it draws is a <see cref="ScreenEntity"/> carrying one renderer, placed in canvas pixels
/// from an <see cref="Anchor"/>: a title on the canvas's top edge and two items on its centre. The
/// shell's render resolution is the cameras' world span, so one canvas pixel is one world pixel and the
/// font draws unscaled.
/// </para>
/// <para>
/// A <see cref="FocusNavigator{T}"/> owns which item is focused, and this scene owns how that reads:
/// its events hand over the focused <see cref="Label"/>, and the scene recolours the items and moves
/// one highlight bar onto the focused item's <see cref="Renderer.Bounds"/>. The bar is a
/// <see cref="ColorRect"/> on an entity of lower <see cref="Entity.ZIndex"/>, so it draws under the
/// labels rather than over them.
/// </para>
/// </summary>
public sealed class MainMenu : Scene
{
    /// <summary>Canvas pixels down from the canvas's top edge to the title's own top edge.</summary>
    private const float TitleMargin = 28f;

    /// <summary>Canvas pixels between the two items' centres.</summary>
    private const float ItemSpacing = 20f;

    /// <summary>
    /// The item's box in canvas pixels: wider and taller than either caption, so it is the hit target
    /// the pointer picks and the bar the highlight covers rather than the glyphs alone.
    /// </summary>
    private static readonly Vector2 ItemBox = new(88f, 16f);

    private static readonly ColorRgba FocusedInk = ColorRgba.Black;
    private static readonly ColorRgba RestingInk = ColorRgba.White;

    private readonly Label _start = Caption("Start");
    private readonly Label _exit = Caption("Exit");

    private readonly ColorRect _bar = new(Vector2.Zero);
    private readonly ScreenEntity _highlight = new(Anchor.TopLeft, Vector2.Zero) { ZIndex = -1 };

    private readonly FocusNavigator<Label> _focus;

    public MainMenu()
    {
        // The actions are the game's, declared once in GameInput and handed to every navigator it drives.
        _focus = new FocusNavigator<Label>(GameInput.MenuFocus, _start, _exit);

        _focus.FocusChanged += Show;
        _focus.Activated += item =>
        {
            if (ReferenceEquals(item, _start))
            {
                RequestScene<Room>();
            }
            else
            {
                RequestExit();
            }
        };
    }

    /// <inheritdoc/>
    protected override void OnStart()
    {
        // The other half of the camera model: a scene with nothing to follow spans the plain camera it
        // is given rather than installing one of its own, so its view is centred on the world origin.
        Camera.ViewportSize = World.ViewportSize;

        // The title measures its own run instead of filling a box, so it hangs from the canvas's top
        // edge by the margin alone.
        ScreenEntity title = new(Anchor.Top, new Vector2(0f, TitleMargin));
        title.Add(new Label(CapsuleAssets.Fonts.Menu, "Minimal Game")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Add(title);

        _highlight.Add(_bar);
        Add(_highlight);

        ScreenEntity start = new(Anchor.Center, new Vector2(0f, -ItemSpacing / 2f));
        start.Add(_start);
        Add(start);

        ScreenEntity exit = new(Anchor.Center, new Vector2(0f, ItemSpacing / 2f));
        exit.Add(_exit);
        Add(exit);

        // The first item takes the focus with no move to report, so the opening state is shown from
        // here; the items are in the scene by now, so their boxes measure.
        Show(_focus.Focused!);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        _focus.Step(context.Input);

        if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }

    // A box, not a bare run: the alignment point is the box's centre, the caption is centred inside it,
    // and Bounds is the whole box whatever the caption measures.
    private static Label Caption(string text) =>
        new(CapsuleAssets.Fonts.Menu, text)
        {
            Size = ItemBox,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Middle,
            Color = RestingInk,
        };

    private void Show(Label focused)
    {
        foreach (Label item in _focus.Items)
        {
            item.Color = ReferenceEquals(item, focused) ? FocusedInk : RestingInk;
        }

        // The highlight is anchored to the canvas's top-left corner, so a position on it is the canvas
        // pixel a bound already names. Teleported, because a bar that interpolated would trail a step
        // behind the focus it marks.
        Rect box = focused.Bounds;
        _highlight.Teleport(box.Position);
        _bar.Size = box.Size;
    }
}
