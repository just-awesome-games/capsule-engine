using System.Runtime.CompilerServices;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class BlendModeTests
{
    // Blend landed in the padding after the two bools: 112 bytes before and after (measured against
    // the pre-change record shape).
    [Fact]
    public void SpriteIntent_KeepsItsSize()
    {
        Assert.Equal(112, Unsafe.SizeOf<SpriteIntent>());
    }

    [Fact]
    public void Additive_PacksZeroAlphaWithTheColourUntouched()
    {
        ColorRgba color = new(200, 120, 40, 180);

        (byte r, byte g, byte b, byte a) = SpriteBatcher.PackInput(color, BlendMode.Additive);

        Assert.Equal((color.R, color.G, color.B, (byte)0), (r, g, b, a));
    }

    [Fact]
    public void Alpha_PacksTheIntentsColourAndAlphaUnchanged()
    {
        ColorRgba color = new(200, 120, 40, 180);

        (byte r, byte g, byte b, byte a) = SpriteBatcher.PackInput(color, BlendMode.Alpha);

        Assert.Equal((color.R, color.G, color.B, color.A), (r, g, b, a));
    }
}
