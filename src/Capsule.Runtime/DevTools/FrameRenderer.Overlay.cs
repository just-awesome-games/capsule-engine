using Capsule.Rendering;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = System.Numerics.Vector2;

namespace Capsule.Runtime.Rendering;

internal sealed partial class FrameRenderer
{
    // Draws a host-owned frame over the game frame already submitted to the back buffer. Its world
    // list comes first, placed where the last game frame's world landed so it annotates that frame's
    // sprites, then its screen layer at an integer scale of the back buffer. The host's own camera is
    // not used here, and the world list is culled against nothing and drawn at the settled step.
    internal void DrawOverlay(FrameView view, int scale)
    {
        PresentationParameters backBuffer = _device.PresentationParameters;
        int width = backBuffer.BackBufferWidth;
        int height = backBuffer.BackBufferHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (_world is { } world && (!view.Sprites.IsEmpty || !view.Lines.IsEmpty))
        {
            DrawOverlayWorld(view, in world, width, height);
        }

        // A view with an empty screen layer draws nothing here and leaves the viewport on the world's
        // region. Otherwise DrawScreen widens it to the full back buffer. The overlay draws with
        // Capsule's own shader whatever its view carries.
        DrawScreen(
            view,
            alpha: 1f,
            new ScreenPlacement(Vector2.Zero, scale),
            width,
            height,
            TextureSampling.Point,
            materials: default,
            clips: default,
            clipLevels: default,
            surfaceFactor: 1);
    }

    // The world's region of the back buffer is the fit inside the surface carried through the
    // surface's placement, and the viewport is narrowed to that region so nothing lands on the bars.
    // With the viewport's origin at the region's corner, the transform maps the world's top-left to
    // the origin and units to back-buffer pixels.
    private void DrawOverlayWorld(FrameView view, in WorldPlacement world, int width, int height)
    {
        Letterbox fit = world.Fit;
        ScreenPlacement present = world.Present;
        int left = (int)MathF.Round(present.Origin.X + (fit.X * present.Scale));
        int top = (int)MathF.Round(present.Origin.Y + (fit.Y * present.Scale));
        int right = Math.Min((int)MathF.Round(present.Origin.X + ((fit.X + fit.Width) * present.Scale)), width);
        int bottom = Math.Min((int)MathF.Round(present.Origin.Y + ((fit.Y + fit.Height) * present.Scale)), height);
        if (left < 0 || top < 0 || right <= left || bottom <= top)
        {
            return;
        }

        _device.Viewport = new Viewport(left, top, right - left, bottom - top);

        float pixelsPerUnit = world.PixelsPerUnit;
        _batcher.Begin(WorldToSurface(world.TopLeft, pixelsPerUnit), SamplerState.PointClamp);

        Pass pass = new()
        {
            Alpha = 1f,
            Snap = true,
            Scale = pixelsPerUnit,
            SnapLines = world.Snap,
            LineScale = world.Fit.Scale,
            LayerCorner = world.TopLeft,
            FrameCorner = world.TopLeft,
        };

        DrawIntents(view.Sprites, view.Lines, default, default, default, default, default, ref pass);
        _batcher.End();
    }
}
