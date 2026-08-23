using Capsule.Rendering;
using Capsule.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

/// <summary>
/// Draws a <see cref="FrameView"/>. Simulations never reach the device; everything
/// they want on screen arrives here as data.
/// </summary>
internal sealed class FrameRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _pixel;

    internal FrameRenderer(GraphicsDevice device)
    {
        _device = device;
        _spriteBatch = new SpriteBatch(device);

        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);
    }

    /// <param name="alpha">
    /// Fraction of a fixed step not yet simulated, in [0, 1). Interpolates previous
    /// to current state; nothing moves yet, so nothing reads it beyond the signature.
    /// </param>
    internal void Draw(FrameView view, ColorRgba clearColor, float alpha)
    {
        _device.Clear(ToXna(clearColor));

        ReadOnlySpan<TextBlock> blocks = view.TextBlocks;
        if (blocks.IsEmpty)
        {
            return;
        }

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        for (int i = 0; i < blocks.Length; i++)
        {
            DrawText(blocks[i]);
        }

        _spriteBatch.End();
    }

    public void Dispose()
    {
        _pixel.Dispose();
        _spriteBatch.Dispose();
    }

    private void DrawText(in TextBlock block)
    {
        Viewport viewport = _device.Viewport;

        // No discard arm: adding an Anchor without a placement must fail the build (CS8509).
#pragma warning disable CS8524 // Anchor has no unnamed values; only a cast can produce one.
        PixelOrigin origin = block.Anchor switch
        {
            Anchor.Center => PixelText.CenterOrigin(block.Layout, block.CellPixels, viewport.Width, viewport.Height),
        };
#pragma warning restore CS8524

        Color color = ToXna(block.Color);
        ReadOnlySpan<GridCell> cells = block.Layout.Cells;

        for (int i = 0; i < cells.Length; i++)
        {
            Rectangle destination = new(
                origin.X + (cells[i].X * block.CellPixels),
                origin.Y + (cells[i].Y * block.CellPixels),
                block.CellPixels,
                block.CellPixels);

            _spriteBatch.Draw(_pixel, destination, color);
        }
    }

    // ColorRgba is straight alpha and the studio blend convention is premultiplied.
    private static Color ToXna(ColorRgba color) =>
        Color.FromNonPremultiplied(color.R, color.G, color.B, color.A);
}
