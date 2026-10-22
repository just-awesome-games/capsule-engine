using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>Another look for a set of frames: each frame mapped to the sprite drawn in its place, or to nothing.</summary>
/// <remarks>
/// A variant is immutable and finds a frame by <see cref="Sprite"/> value equality. A generated
/// variant sheet's <c>Variant</c> member holds one. <c>SpriteRenderer.Variant</c> draws through it,
/// and <see cref="Combine"/> joins the variants of several sheets into one look.
/// </remarks>
/// <example>
/// A hat drawn over every frame of an avatar's sheet and its seated sheet:
/// <code>
/// SpriteVariant hat = SpriteVariant.Combine(
///     CapsuleAssets.Sprites.AvatarHatSheet.Variant,
///     CapsuleAssets.Sprites.AvatarSeatedHatSheet.Variant);
/// </code>
/// </example>
public sealed class SpriteVariant
{
    private readonly Dictionary<Sprite, Sprite?> _drawn;
    private readonly TextureHandle[] _textures;

    /// <summary>Creates a variant drawing <paramref name="drawn"/>[i] in place of <paramref name="frames"/>[i].</summary>
    /// <param name="frames">The frames this variant maps. A frame listed twice maps to one drawing both times.</param>
    /// <param name="drawn">What each frame draws as, one per frame. A null entry draws nothing.</param>
    public SpriteVariant(ReadOnlySpan<Sprite> frames, ReadOnlySpan<Sprite?> drawn)
    {
        if (frames.Length != drawn.Length)
        {
            throw new ArgumentException(
                $"{frames.Length} frame(s) came with {drawn.Length} drawing(s). Pass one drawing per frame.",
                nameof(drawn));
        }

        _drawn = new Dictionary<Sprite, Sprite?>(frames.Length);
        for (int i = 0; i < frames.Length; i++)
        {
            if (_drawn.TryGetValue(frames[i], out Sprite? mapped))
            {
                if (mapped != drawn[i])
                {
                    throw new ArgumentException(
                        $"Frame {i} repeats an earlier frame with another drawing. A frame draws one way in a variant. Give every copy of the frame the same drawing.",
                        nameof(drawn));
                }

                continue;
            }

            _drawn.Add(frames[i], drawn[i]);
        }

        _textures = TexturesOf(_drawn);
    }

    private SpriteVariant(Dictionary<Sprite, Sprite?> drawn)
    {
        _drawn = drawn;
        _textures = TexturesOf(drawn);
    }

    /// <summary>Every texture this variant draws from, each once. An owner preloads them with <see cref="AssetCollection.Add(ReadOnlySpan{TextureHandle})"/>.</summary>
    public ReadOnlySpan<TextureHandle> Textures => _textures;

    /// <summary>Joins <paramref name="variants"/> into one variant mapping every frame each of them maps.</summary>
    /// <param name="variants">The variants to join. No two map the same frame.</param>
    /// <returns>A new variant. Each of <paramref name="variants"/> is unchanged.</returns>
    public static SpriteVariant Combine(params ReadOnlySpan<SpriteVariant> variants)
    {
        int count = 0;
        foreach (SpriteVariant variant in variants)
        {
            ArgumentNullException.ThrowIfNull(variant, nameof(variants));
            count += variant._drawn.Count;
        }

        Dictionary<Sprite, Sprite?> drawn = new(count);
        for (int i = 0; i < variants.Length; i++)
        {
            foreach ((Sprite frame, Sprite? mapped) in variants[i]._drawn)
            {
                if (!drawn.TryAdd(frame, mapped))
                {
                    throw new ArgumentException(
                        $"Variant {i} maps a frame an earlier variant already maps. A frame draws one way in a combined variant. Combine variants of different frames.",
                        nameof(variants));
                }
            }
        }

        return new SpriteVariant(drawn);
    }

    // Whether this variant maps the frame, and what it draws as: null draws nothing.
    internal bool TryGet(Sprite frame, out Sprite? drawn) => _drawn.TryGetValue(frame, out drawn);

    private static TextureHandle[] TexturesOf(Dictionary<Sprite, Sprite?> drawn)
    {
        List<TextureHandle> textures = [];
        foreach (Sprite? mapped in drawn.Values)
        {
            if (mapped is { } sprite && sprite.Texture != default && !textures.Contains(sprite.Texture))
            {
                textures.Add(sprite.Texture);
            }
        }

        return [.. textures];
    }
}
