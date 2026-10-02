using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;

namespace Capsule.Runtime.DevTools;

// The overlay's view of the held world. It stays attached to the game's camera until a pan, a scroll or
// a zoom touches it, then stands in for that camera's view through the scene host. The game's Camera is never
// written. Closing the overlay, the Game Camera row and a change of scene attach it again.
internal sealed class FreeCamera
{
    // Each wheel notch scrolls the view by this share of its visible span on that axis. A share of the span
    // feels the same at every zoom and resolution.
    internal const float ScrollStep = 0.1f;

    // Each Ctrl+wheel notch scales the view by this much, which crosses a screen's detail in a few notches.
    internal const float ZoomStep = 1.25f;

    // The closest the wheel zooms, relative to the game camera's span at detach. Closer shows little
    // more than single texels.
    internal const float MaxZoomIn = 4f;

    // The furthest the wheel zooms out, relative to the game camera's span at detach. Further leaves the
    // world too small to read.
    internal const float MaxZoomOut = 8f;

    private readonly SceneHost _scenes;
    private readonly FixedStepScheduler _scheduler;

    // The view standing in for the game's while detached.
    private CameraView _view;
    private Vector2 _detachSize;
    private bool _detached;

    // The action holding a pan, Pan or Grab, or null while none is. A pan moves the view by the pointer's
    // travel.
    private InputAction? _panAction;
    private Vector2 _lastWindow;

    internal FreeCamera(SceneHost scenes, FixedStepScheduler scheduler)
    {
        _scenes = scenes;
        _scheduler = scheduler;
    }

    internal bool Detached => _detached;

    // How many times wider than the game camera's span at detach the view spans, and 1 while attached.
    internal float ZoomOut => _detached ? _view.Size.X / _detachSize.X : 1f;

    // Reads the frame's gestures while the overlay holds the run, and attaches once it does not. input is
    // the overlay's, advanced to this frame. window is the pointer in back-buffer pixels. overWorld says it
    // stands on the drawn world clear of the menu, and world is where the last frame's world landed.
    internal void Read(InputState input, Vector2 window, bool overWorld, WorldPlacement? world, bool held)
    {
        // A new simulation starts with no stand-in, which is how a scene change shows here.
        if (_detached && _scenes.ViewCamera is null)
        {
            Forget();
        }

        if (!held)
        {
            Attach();

            return;
        }

        // A grab pan ends when either Grab or Click is released.
        if (_panAction is { } action
            && (!input.IsHeld(action) || (action == OverlayActions.Grab && !input.IsHeld(OverlayActions.Click))))
        {
            _panAction = null;
        }

        if (_panAction is null && overWorld)
        {
            if (input.WasPressed(OverlayActions.Pan))
            {
                _panAction = OverlayActions.Pan;
            }
            else if (input.WasPressed(OverlayActions.Click) && input.IsHeld(OverlayActions.Grab))
            {
                _panAction = OverlayActions.Grab;
            }

            if (_panAction is not null)
            {
                _lastWindow = window;
            }
        }

        Vector2 travel = Vector2.Zero;
        if (_panAction is not null)
        {
            travel = window - _lastWindow;
            _lastWindow = window;
        }

        // Zoom turns the wheel into a zoom and ignores the sideways wheel. Sideways turns the vertical wheel
        // sideways, and wheel up moves the view up or left as in a document.
        Vector2 scroll = Vector2.Zero;
        float notches = 0f;
        if (overWorld)
        {
            float vertical = input.Axis(OverlayActions.Scroll);
            float sideways = input.Axis(OverlayActions.ScrollSideways);
            if (input.IsHeld(OverlayActions.Zoom))
            {
                notches = vertical;
            }
            else if (input.IsHeld(OverlayActions.Sideways))
            {
                scroll = new Vector2(sideways - vertical, 0f);
            }
            else
            {
                scroll = new Vector2(sideways, -vertical);
            }
        }

        if (world is not { } placement || !(placement.PixelsPerUnit > 0f)
            || (travel == Vector2.Zero && scroll == Vector2.Zero && notches == 0f))
        {
            return;
        }

        Move(travel, scroll, notches, window, placement);
    }

    // Drops the stand-in and any pending gesture, and the game's own camera frames the view again.
    internal void Attach()
    {
        bool detached = _detached;
        Forget();
        if (detached)
        {
            _scenes.ViewCamera = null;
        }
    }

    private void Forget()
    {
        _detached = false;
        _panAction = null;
    }

    // Pans by the pointer's travel and the scrolled notches, then zooms about the world point under the
    // pointer, which stays put.
    private void Move(Vector2 travel, Vector2 scroll, float notches, Vector2 window, in WorldPlacement placement)
    {
        if (!_detached)
        {
            Detach();
        }

        Vector2 center = _view.Center;
        Vector2 size = _view.Size;
        Vector2 visible = new Vector2(placement.Fit.Width, placement.Fit.Height) / placement.Fit.Scale;
        Vector2 panned = (scroll * ScrollStep * visible) - (travel / placement.PixelsPerUnit);
        center += panned;

        if (notches != 0f)
        {
            float ratio = size.X / _detachSize.X;
            float target = Math.Clamp(ratio * MathF.Pow(ZoomStep, -notches), 1f / MaxZoomIn, MaxZoomOut);
            float scale = target / ratio;

            // The drawn frame predates this frame's pan, which has since carried its point under the pointer.
            if (scale != 1f)
            {
                Vector2 anchor = placement.ToWorld(window) + panned;
                center = anchor + ((center - anchor) * scale);
                size *= scale;
            }
        }

        if (center == _view.Center && size == _view.Size)
        {
            return;
        }

        _view = new CameraView(center, center, size, _view.Fit, Bounds: null, _view.ScrollCenter);
        _scenes.ViewCamera = _view;
    }

    // Seeds the stand-in from the game's view at the held alpha of 1, centred where the game's frame is
    // placed after its bounds and offset. Placed at the same span, it resolves to the same world rect.
    internal void Detach()
    {
        FrameView frame = _scenes.View;
        CameraView game = frame.Camera.At(1f);
        Vector2 span = Span(game, frame.Canvas);
        Vector2 center = game.Center;
        if (game.Bounds is { } bounds)
        {
            center = CameraView.Confine(center, span / 2f, bounds);
        }

        center += game.Offset;

        _view = new CameraView(center, center, game.Size, game.Fit, Bounds: null, game.ScrollCenter);
        _detachSize = game.Size;
        _detached = true;
        _scenes.ViewCamera = _view;
    }

    // The span the host places the game's view at on the run's output, quantised as the renderer
    // quantises it. An output with no area falls back to the declared span.
    private Vector2 Span(in CameraView game, Vector2 canvas)
    {
        int width = (int)_scheduler.Output.X;
        int height = (int)_scheduler.Output.Y;

        return width > 0 && height > 0
            ? FrameLayout.Layout(_scenes.Run.RenderResolution, game, canvas, width, height).Span
            : game.ResolveSpan(_scheduler.Output);
    }
}
