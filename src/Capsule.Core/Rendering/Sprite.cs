using System.Numerics;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>One drawable frame: a region of a texture, the point inside it a position anchors, and the named marks a sheet set on it.</summary>
/// <param name="Texture">The texture the region is cut from.</param>
/// <param name="Region">The region drawn, in texels of <paramref name="Texture"/>.</param>
/// <param name="Pivot">
/// The anchor, in texels from the top-left corner of the frame as it draws, and that corner
/// by default. This texel lands on whatever position the frame is drawn at, and a flip mirrors the
/// region about it. A pivot halfway across an axis flips in place, while a pivot at the edge swings the
/// region across the position.
/// </param>
/// <param name="Marks">
/// The named sockets and boxes this frame carries, in <paramref name="Pivot"/>'s texel space, and none
/// by default. Sprites of one frame share one instance.
/// </param>
/// <param name="FlipX">
/// Whether this frame reads its region's texels mirrored left to right, and false by default. A mirrored
/// drawing then needs no texels of its own. The pivot and marks are measured on the frame as it draws. A
/// flip on whatever draws the frame mirrors it again, about the pivot.
/// </param>
/// <param name="FlipY">Whether this frame reads its region's texels mirrored top to bottom, as <paramref name="FlipX"/> does left to right. False by default.</param>
public readonly record struct Sprite(
    TextureHandle Texture,
    TextureRegion Region,
    Vector2 Pivot = default,
    SpriteMarks? Marks = null,
    bool FlipX = false,
    bool FlipY = false)
{
    /// <summary>
    /// The engine's white texel, anchored at its corner. Flat colour is drawn from this frame,
    /// stretched to whatever extent the intent asks for and tinted by its colour.
    /// </summary>
    public static Sprite White => new(TextureHandle.White, new TextureRegion(0, 0, 1, 1));

    /// <summary>
    /// The engine's radial light, full at the centre and falling to nothing at the edge, anchored at its
    /// centre. A <see cref="LightIntent"/> with no other sprite draws this one.
    /// </summary>
    public static Sprite Light => new(TextureHandle.Light, new TextureRegion(0, 0, 128, 128), new Vector2(64f, 64f));

    /// <summary>
    /// Returns the rect of the box named <paramref name="name"/> on this frame, measured from its
    /// <see cref="Pivot"/>: where the box sits about the position the frame is drawn at.
    /// </summary>
    /// <remarks>
    /// The rect is this one frame's, read once. Nothing mirrors or offsets it as a renderer would, and it
    /// does not follow an animation. A collider that covers the box on whichever frame is drawn is
    /// <c>SpriteRenderer.Box</c>'s.
    /// </remarks>
    /// <example>
    /// A hitbox sized to the box a sheet set on its frame:
    /// <code>
    /// Rect punch = PunchFrame.Box(CapsuleAssets.Sprites.Enemies.BruteSheet.Boxes.Punch);
    /// </code>
    /// </example>
    /// <param name="name">The box's name as the sheet declared it. The sheet's generated <c>Boxes</c> class lists them.</param>
    /// <exception cref="ArgumentException">This frame carries no box named <paramref name="name"/>.</exception>
    public Rect Box(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        ReadOnlySpan<SpriteBox> boxes = Marks is { } marks ? marks.Boxes : default;
        foreach (SpriteBox box in boxes)
        {
            if (box.Name == name)
            {
                return new Rect(box.Area.Position - Pivot, box.Area.Size);
            }
        }

        string carried = boxes.IsEmpty ? "none" : string.Join(", ", boxes.ToArray().Select(static box => box.Name));
        throw new ArgumentException($"This frame carries no box named \"{name}\". Its boxes: {carried}.", nameof(name));
    }

    /// <summary>
    /// Returns the point of the socket named <paramref name="name"/> on this frame, measured from its
    /// <see cref="Pivot"/>: where the socket sits about the position the frame is drawn at.
    /// </summary>
    /// <remarks>
    /// The point is this one frame's, read once. Nothing mirrors or offsets it as a renderer would, and it
    /// does not follow an animation. An entity placed at the socket on whichever frame is drawn is
    /// <c>SpriteRenderer.Socket</c>'s.
    /// </remarks>
    /// <example>
    /// A menu figure lined up by where its muzzle sits on its firing frame:
    /// <code>
    /// Vector2 muzzle = ShootFrame.Socket(CapsuleAssets.Sprites.Actors.HeroSheet.Sockets.Muzzle);
    /// </code>
    /// </example>
    /// <param name="name">The socket's name as the sheet declared it. The sheet's generated <c>Sockets</c> class lists them.</param>
    /// <exception cref="ArgumentException">This frame carries no socket named <paramref name="name"/>.</exception>
    public Vector2 Socket(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        ReadOnlySpan<SpriteSocket> sockets = Marks is { } marks ? marks.Sockets : default;
        foreach (SpriteSocket socket in sockets)
        {
            if (socket.Name == name)
            {
                return socket.Point - Pivot;
            }
        }

        string carried = sockets.IsEmpty ? "none" : string.Join(", ", sockets.ToArray().Select(static socket => socket.Name));
        throw new ArgumentException($"This frame carries no socket named \"{name}\". Its sockets: {carried}.", nameof(name));
    }
}
