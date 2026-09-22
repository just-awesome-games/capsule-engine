using Capsule.Runtime.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.DeviceTests;

public sealed class TextureDecodeTests
{
    [DeviceFact]
    public void EveryPng_UploadsTheTexelsTexture2DFromStreamUploads()
    {
        string[] files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Pngs"), "*.png", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        DeviceThread.Run(() =>
        {
            using Game game = new();
            using GraphicsDeviceManager graphics = new(game);
            game.RunOneFrame();
            TexelPool pool = new();

            foreach (string path in files)
            {
                byte[] expected;
                using (Stream file = File.OpenRead(path))
                using (Texture2D reference = Texture2D.FromStream(game.GraphicsDevice, file, DefaultColorProcessors.PremultiplyAlpha))
                {
                    expected = new byte[reference.Width * reference.Height * 4];
                    reference.GetData(expected);
                }

                byte[] actual = new byte[expected.Length];
                using (Stream decoding = File.OpenRead(path))
                {
                    DecodedTexture decoded = TextureDecoder.Decode(decoding, pool, Path.GetFileName(path));
                    TextureStore.TextureUpload upload = new(game.GraphicsDevice, pool, decoded);

                    long budget;
                    do
                    {
                        budget = 3L * decoded.Width * 4;
                    }
                    while (!upload.Advance(ref budget));

                    using Texture2D uploaded = upload.Finish();
                    uploaded.GetData(actual);
                }

                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{Path.GetFileName(path)} uploads other texels than Texture2D.FromStream does.");
            }
        });
    }
}
