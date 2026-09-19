using System.Numerics;

namespace Capsule.Runtime.Rendering;

// The four corners of one drawn sprite and the texture coordinates at its top-left and bottom-right, in
// the space the batch's transform maps to the surface. Computed operation for operation as MonoGame
// 3.8.5.1's SpriteBatch.Draw and SpriteBatchItem.Set compute them: the same float expressions in the
// same order, with sines and cosines from MathF. A frame the batcher draws is byte-identical to the
// frame SpriteBatch drew from the same intent. Rearranging an expression here, including the double
// division shape, moves the last bit of a vertex and with it pixels.
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

        if (rotation == 0f)
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
        float sin = MathF.Sin(rotation);
        float cos = MathF.Cos(rotation);
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
