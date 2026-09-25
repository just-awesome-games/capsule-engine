using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Assets;

// A resolved handle: the texture to sample and where the handle's texel (0, 0) is on it. Offsets are
// zero for a texture served from its own file, and the page's offset for one the build packed.
internal readonly record struct TextureSlice(Texture2D Texture, int OffsetX, int OffsetY);

// The textures owned by the current scene, preloaded where declared and otherwise loaded on first
// draw. Residency is per file. A packed handle's file is its page, which loads once for any member and
// stays while any member of the incoming scene wants it.
internal sealed class TextureStore : IDisposable
{
    // The texel bytes a prefetch uploads a frame, in whole rows of one page. Lower it if upload frames
    // show in intervalMs max, and raise it if prefetched pages are not ready by their boundary.
    private const long UploadBytesPerFrame = 4 * 1024 * 1024;

    private readonly SceneAssetStore<TextureHandle, Texture2D> _textures;

    private readonly AtlasMap _atlases;

    private readonly HostPlatform _platform;

    internal TextureStore(GraphicsDevice device, HostPlatform platform)
    {
        _platform = platform;
        _atlases = AtlasMap.Load(platform);
        TexelPool pool = new();
        _textures = new(Decode, UploadBytesPerFrame);

        // The batch blends premultiplied, and a straight-alpha texture would fringe dark along
        // every soft edge. A packed page ships straight like any other file.
        TextureUpload Decode(TextureHandle handle)
        {
            using Stream file = TextureFiles.Open(platform, handle);
            return new TextureUpload(device, pool, TextureDecoder.Decode(file, pool, handle.Name));
        }
    }

    // Missing preloads are decoded before the prior scene's textures are released.
    internal void ChangeScene(AssetCollection preloads, Action prepareRemainingAssets) =>
        _textures.ChangeScene(_atlases.Residency(preloads.Textures), prepareRemainingAssets);

    internal void Prefetch(AssetCollection preloads) => _textures.Prefetch(_atlases.Residency(preloads.Textures));

    internal void Pump() => _textures.Pump();

    // Loads on first use when the scene did not preload the handle.
    internal TextureSlice Get(in TextureHandle handle) =>
        _atlases.TryGet(handle, out AtlasSlot slot)
            ? new TextureSlice(Get(slot.Page, handle), slot.X, slot.Y)
            : new TextureSlice(Get(handle, handle), 0, 0);

    // The handle's own file, for a material that binds it whole. A packed handle has no file of its
    // own, and its page would bind every other member with it.
    internal Texture2D GetWhole(in TextureHandle handle)
    {
        if (_atlases.TryGet(handle, out _))
        {
            throw new InvalidOperationException(
                $"Texture '{handle.Name}' is packed into an atlas, and a material binds its textures whole. Remove it from every atlas manifest.");
        }

        return Get(handle, handle);
    }

    // The handle's texels inside region as straight RGBA, row by row, decoded from its file or its
    // page. Residency is untouched, so a caller outside the frame path can read any handle. Throws
    // when the file is missing or the region falls outside the handle's texels.
    internal byte[] ReadRegion(in TextureHandle handle, TextureRegion region) =>
        ReadRegion(_platform, _atlases, handle, region);

    internal static byte[] ReadRegion(HostPlatform platform, AtlasMap atlases, in TextureHandle handle, TextureRegion region)
    {
        (TextureHandle file, int offsetX, int offsetY) = atlases.TryGet(handle, out AtlasSlot slot)
            ? (slot.Page, slot.X, slot.Y)
            : (handle, 0, 0);

        TexelPool pool = new();
        DecodedTexture decoded;
        using (Stream stream = TextureFiles.Open(platform, file))
        {
            decoded = TextureDecoder.Decode(stream, pool, handle.Name);
        }

        int left = offsetX + region.X;
        int top = offsetY + region.Y;
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || left + region.Width > decoded.Width || top + region.Height > decoded.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                region,
                $"The region falls outside the {decoded.Width} by {decoded.Height} texels of '{handle.Name}'. Cut the sprite from inside its texture.");
        }

        byte[] texels = new byte[region.Width * region.Height * 4];
        int rowBytes = region.Width * 4;
        for (int row = 0; row < region.Height; row++)
        {
            int from = (((top + row) * decoded.Width) + left) * 4;
            decoded.Texels.AsSpan(from, rowBytes).CopyTo(texels.AsSpan(row * rowBytes, rowBytes));
        }

        pool.Return(decoded.Texels);
        TextureDecoder.Unpremultiply(texels);

        return texels;
    }

    public void Dispose() => _textures.Dispose();

    private Texture2D Get(in TextureHandle file, in TextureHandle drawn)
    {
        if (_textures.TryGet(file, out Texture2D texture))
        {
            return texture;
        }

        texture = _textures.Load(file);
        Log.Info($"'{drawn.Name}' loaded on first draw. Declare it to preload it");

        return texture;
    }

    // One decoded page on its way to the device, top rows first.
    internal sealed class TextureUpload(GraphicsDevice device, TexelPool pool, DecodedTexture decoded) : IPendingAsset<Texture2D>
    {
        private Texture2D? _texture;
        private int _rows;

        public bool Advance(ref long budget)
        {
            int rowBytes = decoded.Width * 4;
            int rows = (int)Math.Clamp(budget / rowBytes, 0, decoded.Height - _rows);
            if (rows == 0)
            {
                return false;
            }

            Upload(rows);
            budget -= (long)rows * rowBytes;

            return _rows == decoded.Height;
        }

        public Texture2D Finish()
        {
            try
            {
                if (_rows < decoded.Height)
                {
                    Upload(decoded.Height - _rows);
                }
            }
            catch
            {
                Discard();
                throw;
            }

            Texture2D texture = _texture!;
            _texture = null;
            pool.Return(decoded.Texels);

            return texture;
        }

        public void Discard()
        {
            _texture?.Dispose();
            _texture = null;
            pool.Return(decoded.Texels);
        }

        // A whole-texture SetData of a 4096-texel page measured twice its rows written as slices.
        private void Upload(int rows)
        {
            _texture ??= new Texture2D(device, decoded.Width, decoded.Height);
            int rowBytes = decoded.Width * 4;
            _texture.SetData(0, new Rectangle(0, _rows, decoded.Width, rows), decoded.Texels, _rows * rowBytes, rows * rowBytes);
            _rows += rows;
        }
    }
}
