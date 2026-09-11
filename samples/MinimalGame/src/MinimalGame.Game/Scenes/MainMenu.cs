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
/// Everything it draws lives on the frame's screen layer, in canvas pixels from an
/// <see cref="Anchor"/>: a title on the canvas's top edge and two items on its centre. The shell's
/// render resolution is the cameras' world span, so one canvas pixel is one world pixel and the font
/// draws unscaled.
/// </para>
/// <para>
/// A <see cref="FocusNavigator"/> owns which item is focused, and this scene owns how that reads: the
/// focused item's colour, and the one highlight bar moved to its <see cref="Renderer.Bounds"/>. The bar
/// is a <see cref="ColorRect"/> on an entity of lower <see cref="Entity.ZIndex"/>, so it draws under
/// the labels rather than over them.
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

    private readonly ScreenText _start = new(new Vector2(0f, -ItemSpacing / 2f), "Start");
    private readonly ScreenText _exit = new(new Vector2(0f, ItemSpacing / 2f), "Exit");
    private readonly Highlight _highlight = new();

    private readonly FocusNavigator _focus = new();

    /// <inheritdoc/>
    protected override void OnStart()
    {
        // The other half of the camera model: a scene with nothing to follow spans the plain camera it
        // is given rather than installing one of its own, so its view is centred on the world origin.
        Camera.ViewportSize = World.ViewportSize;

        // The title is the same screen-space text with the box taken off it, so it measures its own run
        // and hangs from the canvas's top edge.
        ScreenText title = new(new Vector2(0f, TitleMargin), "Minimal Game") { Anchor = Anchor.Top };
        title.Caption.Size = Vector2.Zero;
        title.Caption.VerticalAlignment = VerticalAlignment.Top;
        Add(title);

        Add(_highlight);
        Add(_start);
        Add(_exit);

        // List order is the order the focus walks and the order the pointer hit-tests in.
        _focus.Add(_start.Caption);
        _focus.Add(_exit.Caption);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        _focus.Step(context.Input, GameInput.MenuUp, GameInput.MenuDown, GameInput.Confirm, GameInput.Click);

        if (_focus.Focused is { } focused)
        {
            _start.Caption.Color = ReferenceEquals(focused, _start.Caption) ? FocusedInk : RestingInk;
            _exit.Caption.Color = ReferenceEquals(focused, _exit.Caption) ? FocusedInk : RestingInk;
            _highlight.Cover(focused.Bounds);
        }

        if (_focus.Activated)
        {
            if (ReferenceEquals(_focus.Focused, _start.Caption))
            {
                RequestScene<Room>();
            }
            else
            {
                RequestExit();
            }
        }
        else if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }

    // Not spawnable: neither of these carries an EntitySpawn constructor, so no document can name them
    // and the scene that wants them adds them itself.
    private sealed class ScreenText : Entity
    {
        internal ScreenText(Vector2 position, string text)
            : base(position)
        {
            Space = RenderSpace.Screen;
            Anchor = Anchor.Center;

            // A box, not a bare run: the alignment point is the box's centre, the caption is centred
            // inside it, and Bounds is the whole box whatever the caption measures.
            Caption = new Label(CapsuleAssets.Fonts.Menu, text)
            {
                Size = ItemBox,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Middle,
                Color = RestingInk,
            };

            Add(Caption);
        }

        internal Label Caption { get; }
    }

    private sealed class Highlight : Entity
    {
        internal Highlight()
            : base(Vector2.Zero)
        {
            Space = RenderSpace.Screen;

            // Under every label: the bar and the captions sit on the same layer, so the band is what
            // orders them.
            ZIndex = -1;

            Add(Bar);
        }

        private ColorRect Bar { get; } = new(Vector2.Zero);

        /// <summary>
        /// Puts the bar exactly on <paramref name="bounds"/>. The anchor is the canvas's top-left
        /// corner, so a position here is the canvas pixel a bound already names; teleported, because
        /// a bar that interpolated would trail a step behind the focus it marks.
        /// </summary>
        internal void Cover(ViewBounds bounds)
        {
            Teleport(new Vector2(bounds.Left, bounds.Top));
            Bar.Size = new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
    }
}
