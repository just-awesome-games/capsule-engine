using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Runtime.Assets;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Capsule.Runtime.Input;

// What the window shows for the cursor on one frame. Factor is the image's whole-number scale, and 0
// with no image.
internal readonly record struct CursorLook(bool Shown, Sprite? Image, bool Confined, int Factor);

// Applies the run's cursor to the window. The only place the pointer is shown, hidden, replaced or
// confined. One instance per windowed scene run. A headless run constructs none.
internal sealed class CursorApplier(GraphicsDevice device, TextureStore textures, Action<bool> show, Action<bool> confine) : IDisposable
{
    // SDL accepts larger cursors, but some platforms clip or refuse them.
    internal const int MaxSidePixels = 256;

    // Built on first use and kept until the host is disposed, one per image and factor. A failed
    // build is kept as null and shows the system arrow without retrying.
    private readonly Dictionary<CursorKey, MouseCursor?> _built = [];

    // The backend's window starts with the pointer hidden and ungrabbed. The impossible factor makes
    // the first frame set the cursor.
    private CursorLook _applied = new(Shown: false, Image: null, Confined: false, Factor: -1);

    // The cursor the window shows on this frame. The overlay points with the system arrow, and the
    // pad hides the pointer whatever the game asked for.
    internal static CursorLook Resolve(Cursor cursor, bool padActive, bool overlayOpen, float layerScale)
    {
        if (overlayOpen)
        {
            return new CursorLook(Shown: true, Image: null, Confined: false, Factor: 0);
        }

        Sprite? image = cursor.Image;
        int factor = image is { } sprite ? ScaleFactor(layerScale, sprite.Region) : 0;

        return new CursorLook(cursor.Visible && !padActive, image, cursor.Confined, factor);
    }

    // The layer's scale rounded to a whole number, at least 1, and lowered until the region's longer
    // side fits MaxSidePixels. A region already longer than that stays at 1.
    internal static int ScaleFactor(float layerScale, TextureRegion region)
    {
        int side = Math.Max(region.Width, region.Height);
        float rounded = float.IsFinite(layerScale) ? MathF.Round(layerScale, MidpointRounding.AwayFromZero) : 1f;
        int fitting = Math.Max(1, MaxSidePixels / Math.Max(1, side));

        return (int)Math.Clamp(rounded, 1f, fitting);
    }

    // Called once per frame after the steps. Only what changed since the last frame reaches the window.
    internal void Apply(Cursor cursor, bool padActive, bool overlayOpen, float layerScale)
    {
        CursorLook look = Resolve(cursor, padActive, overlayOpen, layerScale);
        if (look == _applied)
        {
            return;
        }

        if (look.Shown != _applied.Shown)
        {
            show(look.Shown);
        }

        if (look.Image != _applied.Image || look.Factor != _applied.Factor)
        {
            MouseCursor? image = look.Image is { } sprite ? Build(sprite, look.Factor) : null;
            Mouse.SetCursor(image ?? MouseCursor.Arrow);
        }

        if (look.Confined != _applied.Confined)
        {
            confine(look.Confined);
        }

        _applied = look;
    }

    // The window stops showing a built cursor before it is freed.
    public void Dispose()
    {
        if (_built.Count == 0)
        {
            return;
        }

        Mouse.SetCursor(MouseCursor.Arrow);
        foreach (MouseCursor? built in _built.Values)
        {
            built?.Dispose();
        }

        _built.Clear();
    }

    private MouseCursor? Build(Sprite sprite, int factor)
    {
        CursorKey key = new(sprite.Texture, sprite.Region, (int)MathF.Floor(sprite.Pivot.X), (int)MathF.Floor(sprite.Pivot.Y), factor);
        if (_built.TryGetValue(key, out MouseCursor? cached))
        {
            return cached;
        }

        MouseCursor? built = null;
        try
        {
            byte[] texels = Enlarge(textures.ReadRegion(sprite.Texture, sprite.Region), sprite.Region.Width, sprite.Region.Height, factor);
            using Texture2D texture = new(device, sprite.Region.Width * factor, sprite.Region.Height * factor);
            texture.SetData(texels);
            built = MouseCursor.FromTexture2D(texture, key.HotX * factor, key.HotY * factor);
        }
        catch (Exception failure)
        {
            Log.Warning($"The cursor image from '{sprite.Texture.Name}' could not be built. The system arrow shows in its place: {failure.Message}");
        }

        _built.Add(key, built);

        return built;
    }

    // Nearest-neighbour: each texel becomes a factor by factor block.
    private static byte[] Enlarge(byte[] texels, int width, int height, int factor)
    {
        if (factor == 1)
        {
            return texels;
        }

        int scaledWidth = width * factor;
        byte[] scaled = new byte[scaledWidth * height * factor * 4];
        for (int y = 0; y < height * factor; y++)
        {
            int from = (y / factor) * width;
            for (int x = 0; x < scaledWidth; x++)
            {
                texels.AsSpan((from + (x / factor)) * 4, 4).CopyTo(scaled.AsSpan(((y * scaledWidth) + x) * 4, 4));
            }
        }

        return scaled;
    }

    private readonly record struct CursorKey(TextureHandle Texture, TextureRegion Region, int HotX, int HotY, int Factor);
}
