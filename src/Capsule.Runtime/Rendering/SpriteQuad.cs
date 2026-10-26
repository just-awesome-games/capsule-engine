using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Runtime.Rendering;

// The four corners of one drawn sprite and the texture coordinates at its top-left and bottom-right, in
// the space the draw's transform maps to the surface. The float expressions, their order and the sines
// and cosines from MathF are fixed. Rearranging an expression here, including the double division
// shape, moves the last bit of a vertex and with it the pixels the renderer's tests pin.
internal readonly struct SpriteQuad
{
    public readonly Vector2 TopLeft;

    public readonly Vector2 TopRight;

    public readonly Vector2 BottomLeft;

    public readonly Vector2 BottomRight;

    public readonly Vector2 TexTopLeft;

    public readonly Vector2 TexBottomRight;

    private SpriteQuad(
        Vector2 topLeft,
        Vector2 topRight,
        Vector2 bottomLeft,
        Vector2 bottomRight,
        Vector2 texTopLeft,
        Vector2 texBottomRight)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomLeft = bottomLeft;
        BottomRight = bottomRight;
        TexTopLeft = texTopLeft;
        TexBottomRight = texBottomRight;
    }

    // Where the sprite vertex stage puts sprite under uniforms, the CPU's copy of its arithmetic: the
    // sprite interpolated by the frame's fraction, moved from its layer's corner onto the frame's, snapped
    // to whole pixels where the grid is set, and placed. A quad snapped at a whole number of quarter
    // turns has every edge on the grid. A whole texture stands where it is.
    internal static SpriteQuad Expand(in SpriteInstance sprite, in SpriteUniforms uniforms)
    {
        int regionX = (int)(sprite.Region & 0xFFFF);
        int regionY = (int)(sprite.Region >> 16);
        int width = (int)(sprite.Extent & 0xFFFF);
        int height = (int)(sprite.Extent >> 16);
        bool flipX = (sprite.Flags & SpriteFlags.FlipX) != 0;
        bool flipY = (sprite.Flags & SpriteFlags.FlipY) != 0;
        float texelWidth = uniforms.TexelSize.X;
        float texelHeight = uniforms.TexelSize.Y;

        if ((sprite.Flags & SpriteFlags.Whole) != 0)
        {
            return PlaceWhole(sprite.Position, sprite.Origin, sprite.Size, width, height, sprite.Rotation);
        }

        Vector2 position = StepInterpolation.Interpolate(sprite.PreviousPosition, sprite.Position, uniforms.Alpha);
        float rotation = StepInterpolation.Interpolate(sprite.PreviousRotation, sprite.Rotation, uniforms.Alpha);
        Vector2 texels = new(width, height);

        if (uniforms.Grid > 0f
            && ScrollLayout.TryPlaceSnapped(position, sprite.Origin * (sprite.Size / texels), sprite.Size, rotation, uniforms.LayerCorner, uniforms.FrameCorner, uniforms.Grid, out Vector2 corner, out Vector2 pixels, out int turns))
        {
            Vector2 scale = pixels / (texels * uniforms.Grid);

            return turns == 0
                ? Place(corner, Vector2.Zero, scale, regionX, regionY, width, height, texelWidth, texelHeight, 0f, flipX, flipY)
                : PlaceQuarterTurned(corner, scale, regionX, regionY, width, height, texelWidth, texelHeight, turns, flipX, flipY);
        }

        position = ScrollLayout.Place(position, uniforms.LayerCorner, uniforms.FrameCorner, uniforms.Grid > 0f, uniforms.Grid);

        return Place(position, sprite.Origin, sprite.Size / texels, regionX, regionY, width, height, texelWidth, texelHeight, rotation, flipX, flipY);
    }

    // A region of a texture: position is where origin lands, origin is in region texels, scale is drawn
    // units per texel on each axis, the region is in texels of a texture whose texel is texelWidth by
    // texelHeight (one over its extent), and rotation is radians clockwise about origin. A flip swaps
    // the texture coordinates on that axis and leaves the corners in place.
    internal static SpriteQuad Place(
        Vector2 position,
        Vector2 origin,
        Vector2 scale,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight,
        float texelWidth,
        float texelHeight,
        float rotation,
        bool flipX,
        bool flipY)
    {
        Vector2 texTopLeft = new(regionX * texelWidth, regionY * texelHeight);
        Vector2 texBottomRight = new((regionX + regionWidth) * texelWidth, (regionY + regionHeight) * texelHeight);

        return Place(position, origin, scale, regionWidth, regionHeight, texTopLeft, texBottomRight, rotation, flipX, flipY);
    }

    // Place for a region turned clockwise by a whole number of quarter turns about its top-left
    // corner, with the turn's exact sine and cosine. A float quarter turn's cosine is not zero, and it
    // would tilt the corners off the edges the caller snapped.
    internal static SpriteQuad PlaceQuarterTurned(
        Vector2 position,
        Vector2 scale,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight,
        float texelWidth,
        float texelHeight,
        int turns,
        bool flipX,
        bool flipY)
    {
        Vector2 texTopLeft = new(regionX * texelWidth, regionY * texelHeight);
        Vector2 texBottomRight = new((regionX + regionWidth) * texelWidth, (regionY + regionHeight) * texelHeight);
        (float sin, float cos) = turns switch
        {
            1 => (1f, 0f),
            2 => (0f, -1f),
            3 => (-1f, 0f),
            _ => (0f, 1f),
        };

        return Place(position, Vector2.Zero, scale, regionWidth, regionHeight, texTopLeft, texBottomRight, turns != 0, sin, cos, flipX, flipY);
    }

    // A full texture of width by height texels. Its texture coordinates are the literal corners, not
    // width times one over width, which is not always one.
    internal static SpriteQuad PlaceWhole(Vector2 position, Vector2 origin, Vector2 scale, int width, int height, float rotation) =>
        Place(position, origin, scale, width, height, Vector2.Zero, Vector2.One, rotation, flipX: false, flipY: false);

    private static SpriteQuad Place(
        Vector2 position,
        Vector2 origin,
        Vector2 scale,
        int regionWidth,
        int regionHeight,
        Vector2 texTopLeft,
        Vector2 texBottomRight,
        float rotation,
        bool flipX,
        bool flipY) =>
        rotation == 0f
            ? Place(position, origin, scale, regionWidth, regionHeight, texTopLeft, texBottomRight, turned: false, 0f, 1f, flipX, flipY)
            : Place(position, origin, scale, regionWidth, regionHeight, texTopLeft, texBottomRight, turned: true, MathF.Sin(rotation), MathF.Cos(rotation), flipX, flipY);

    private static SpriteQuad Place(
        Vector2 position,
        Vector2 origin,
        Vector2 scale,
        int regionWidth,
        int regionHeight,
        Vector2 texTopLeft,
        Vector2 texBottomRight,
        bool turned,
        float sin,
        float cos,
        bool flipX,
        bool flipY)
    {
        origin *= scale;
        float w = regionWidth * scale.X;
        float h = regionHeight * scale.Y;

        if (flipY)
        {
            (texTopLeft.Y, texBottomRight.Y) = (texBottomRight.Y, texTopLeft.Y);
        }

        if (flipX)
        {
            (texTopLeft.X, texBottomRight.X) = (texBottomRight.X, texTopLeft.X);
        }

        if (!turned)
        {
            float x = position.X - origin.X;
            float y = position.Y - origin.Y;

            return new SpriteQuad(
                new Vector2(x, y),
                new Vector2(x + w, y),
                new Vector2(x, y + h),
                new Vector2(x + w, y + h),
                texTopLeft,
                texBottomRight);
        }

        float dx = 0f - origin.X;
        float dy = 0f - origin.Y;
        float px = position.X;
        float py = position.Y;

        return new SpriteQuad(
            new Vector2(px + (dx * cos) - (dy * sin), py + (dx * sin) + (dy * cos)),
            new Vector2(px + ((dx + w) * cos) - (dy * sin), py + ((dx + w) * sin) + (dy * cos)),
            new Vector2(px + (dx * cos) - ((dy + h) * sin), py + (dx * sin) + ((dy + h) * cos)),
            new Vector2(px + ((dx + w) * cos) - ((dy + h) * sin), py + ((dx + w) * sin) + ((dy + h) * cos)),
            texTopLeft,
            texBottomRight);
    }
}
