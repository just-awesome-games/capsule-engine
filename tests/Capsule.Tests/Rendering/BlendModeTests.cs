using System.Runtime.CompilerServices;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class BlendModeTests
{
    // Blend landed in the padding after the two bools, and the flash in the padding after Blend: 112
    // bytes before and after each (measured against the pre-change record shape).
    [Fact]
    public void SpriteIntent_KeepsItsSize()
    {
        Assert.Equal(112, Unsafe.SizeOf<SpriteIntent>());
    }

    // Additive premultiplies by alpha, so a glow fades as its alpha falls. Opaque adds the colour exactly.
    [Fact]
    public void Additive_PacksTheColourScaledByItsAlphaWithZeroAlpha()
    {
        (byte r, byte g, byte b, byte a) = SpriteBatcher.PackInput(new ColorRgba(200, 120, 40, 180), BlendMode.Additive);

        Assert.Equal(((byte)141, (byte)85, (byte)28, (byte)0), (r, g, b, a));
        Assert.Equal(((byte)200, (byte)120, (byte)40, (byte)0), SpriteBatcher.PackInput(new ColorRgba(200, 120, 40), BlendMode.Additive));
    }

    [Fact]
    public void Alpha_PacksTheIntentsColourAndAlphaUnchanged()
    {
        ColorRgba color = new(200, 120, 40, 180);

        (byte r, byte g, byte b, byte a) = SpriteBatcher.PackInput(color, BlendMode.Alpha);

        Assert.Equal((color.R, color.G, color.B, color.A), (r, g, b, a));
    }

    // The flash colour fades with the sprite's tint alpha and the amount passes through, so a fading
    // sprite's flash fades with it under either blend. An unflashed sprite packs zero, which the shader
    // leaves exactly as the fragment returned it.
    [Fact]
    public void Flash_PacksItsColourScaledByTheTintAlpha_AndItsAmountUnchanged()
    {
        Assert.Equal(((byte)128, (byte)64, (byte)0, (byte)200), SpriteBatcher.PackFlashInput(new ColorRgba(255, 128, 0, 200), 128));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), SpriteBatcher.PackFlashInput(default, 255));
    }
}
