using System.Runtime.CompilerServices;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Rendering;

public sealed class BlendModeTests
{
    // Blend landed in the padding after the two bools, and the flash in the padding after Blend. A
    // sprite's marks are one reference, which took the intent from 112 bytes to 104.
    [Fact]
    public void SpriteIntent_KeepsItsSize()
    {
        Assert.Equal(104, Unsafe.SizeOf<SpriteIntent>());
    }

    // Additive premultiplies by alpha and packs alpha zero. A glow fades as its alpha falls. Opaque adds
    // the colour exactly.
    [Fact]
    public void Additive_PacksTheColourScaledByItsAlphaWithZeroAlpha()
    {
        Assert.Equal((141, 85, 28, 0), Bytes(SpriteDrawList.Tint(new ColorRgba(200, 120, 40, 180), BlendMode.Additive)));
        Assert.Equal((200, 120, 40, 0), Bytes(SpriteDrawList.Tint(new ColorRgba(200, 120, 40), BlendMode.Additive)));
    }

    // Alpha premultiplies the colour and keeps the alpha, truncating each channel.
    [Fact]
    public void Alpha_PacksTheColourPremultipliedByItsAlpha()
    {
        Assert.Equal((141, 84, 28, 180), Bytes(SpriteDrawList.Tint(new ColorRgba(200, 120, 40, 180), BlendMode.Alpha)));
    }

    // The flash colour fades with the sprite's tint alpha and the amount passes through. A fading
    // sprite's flash fades with it under either blend. An unflashed sprite packs zero, which the shader
    // leaves exactly as the fragment returned it.
    [Fact]
    public void Flash_PacksItsColourScaledByTheTintAlpha_AndItsAmountUnchanged()
    {
        Assert.Equal((128, 64, 0, 200), Bytes(SpriteDrawList.Flash(new ColorRgba(255, 128, 0, 200), 128)));
        Assert.Equal(0u, SpriteDrawList.Flash(new ColorRgba(255, 128, 0, 0), 255));
    }

    private static (int R, int G, int B, int A) Bytes(uint packed) =>
        ((byte)packed, (byte)(packed >> 8), (byte)(packed >> 16), (byte)(packed >> 24));
}
