using System.Security.Cryptography;
using Capsule.Runtime.Assets;

namespace Capsule.Tests.Runtime;

// The hashes are the texels Texture2D.FromStream uploaded with PremultiplyAlpha when the decoder
// replaced it. A changed hash is a visible change to every shipped texture.
public sealed class TextureDecodeTests
{
    [Theory]
    [InlineData("alpha-ramp.png", 256, 6, "61DE40409A9BBE13A6E27012AED7EBF96F7B0F23E6EED86A89D8AD9C3B566207")]
    [InlineData("hd-atlas.png", 2048, 2048, "228A495AD750CE525CB57F728FD453091B3E092240B0A1A8089425FBAE61AA6E")]
    public void Decode_ReproducesTheCapturedTexels(string file, int width, int height, string sha256)
    {
        using Stream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Runtime", "Fixtures", file));

        DecodedTexture decoded = TextureDecoder.Decode(stream, new TexelPool(), file);

        Assert.Equal((width, height), (decoded.Width, decoded.Height));
        Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(decoded.Texels)));
    }
}
