using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;

namespace Capsule.Tests.Runtime;

public sealed class GpuReadbackTests
{
    // A capture reads a surface in the swapchain's format, which stores a texel's channels in either
    // byte order. Its PNG holds the colours the surface does, whichever order that is.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodePng_WritesTheSurfacesColoursInEitherByteOrder(bool bgra)
    {
        const int Width = 3;
        const int Height = 2;
        byte[] rgba = new byte[Width * Height * 4];
        for (int texel = 0; texel < Width * Height; texel++)
        {
            rgba[(texel * 4) + 0] = (byte)(10 + texel);
            rgba[(texel * 4) + 1] = (byte)(100 + texel);
            rgba[(texel * 4) + 2] = (byte)(200 + texel);
            rgba[(texel * 4) + 3] = 255;
        }

        byte[] stored = (byte[])rgba.Clone();
        GpuTextureFormat format = bgra ? GpuTextureFormat.B8G8R8A8Unorm : GpuTextureFormat.R8G8B8A8Unorm;
        if (bgra)
        {
            for (int texel = 0; texel < Width * Height; texel++)
            {
                (stored[texel * 4], stored[(texel * 4) + 2]) = (stored[(texel * 4) + 2], stored[texel * 4]);
            }
        }

        byte[] png = GpuReadback.EncodePng(stored, Width, Height, format);

        using MemoryStream stream = new(png);
        using DecodedTexture decoded = TextureDecoder.Decode(stream, "capture");
        Assert.Equal((Width, Height), (decoded.Width, decoded.Height));
        Assert.Equal(rgba, decoded.Texels.ToArray());
    }
}
