using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Runtime.Gpu;
using Capsule.Runtime.Rendering;

namespace Capsule.Runtime.Assets;

// A resolved handle: the texture to sample and where the handle's texel (0, 0) is on it. Offsets are
// zero for a texture served from its own file, and the page's offset for one the build packed.
internal readonly record struct TextureSlice(SampledTexture Texture, int OffsetX, int OffsetY);

// A texture on the device and the sampler its own sampling asks for, or null to sample as the draw
// does.
internal sealed class SampledTexture(GpuTexture texture, GpuSampler? sampler = null) : IDisposable
{
    internal GpuTexture Texture { get; } = texture;

    internal GpuSampler? Sampler { get; } = sampler;

    internal bool Disposed { get; private set; }

    internal static GpuTextureFormat FormatOf(in DecodedTexture decoded) =>
        decoded.BytesPerTexel == 1 ? GpuTextureFormat.R8Unorm : GpuTextureFormat.R8G8B8A8Unorm;

    public void Dispose()
    {
        Texture.Dispose();
        Disposed = true;
    }
}

// The textures owned by the current scene, preloaded where declared and otherwise loaded on first
// draw. Every upload lands in a frame's copy pass: a texture created outside one keeps its texels until
// the next. Residency is per file. A packed handle's file is its page, which loads once for any member and
// stays while any member of the incoming scene wants it.
internal sealed class TextureStore : IDisposable
{
    // The texel bytes a prefetch uploads a frame, in whole rows of one page. Lower it if upload frames
    // show in intervalMs max, and raise it if prefetched pages are not ready by their boundary.
    private const long UploadBytesPerFrame = 4 * 1024 * 1024;

    private static readonly AssetFiles Files = new("Texture", "handle");

    private readonly SceneAssetStore<TextureHandle, SampledTexture> _textures;
    private readonly TextureMap _map;
    private readonly HostPlatform _platform;
    private readonly GpuDevice _device;
    private readonly TexelPool _pool = new();

    // The textures created outside a copy pass, uploaded in the next frame's.
    private readonly List<(TextureUpload Upload, SampledTexture Texture)> _deferred = [];

    // The frame's copy pass while a pump runs in it.
    private GpuCopyPass? _copy;

    internal TextureStore(GpuDevice device, TextureSamplers samplers, HostPlatform platform)
    {
        _device = device;
        _platform = platform;
        _map = TextureMap.Load(platform);
        _textures = new(Decode, UploadBytesPerFrame);

        // Sprites blend premultiplied, and a straight-alpha texture would fringe dark along
        // every soft edge. A packed page ships straight like any other file. An r8 file's value is
        // coverage, which the sprite shader premultiplies itself.
        TextureUpload Decode(TextureHandle handle)
        {
            TextureFacts facts = _map.Facts(handle);
            using Stream file = Open(platform, handle);
            return new TextureUpload(this, TextureDecoder.Decode(file, _pool, handle.Name, facts.SingleChannel), facts.Sampling is { } sampling ? samplers.Get(sampling) : null);
        }
    }

    // Missing preloads are decoded before the prior scene's textures are released.
    internal void ChangeScene(AssetCollection preloads, Action prepareRemainingAssets) =>
        _textures.ChangeScene(_map.Residency(preloads.Textures), prepareRemainingAssets);

    internal void Prefetch(AssetCollection preloads) => _textures.Prefetch(_map.Residency(preloads.Textures));

    // A texture of decoded's texels, sampled as the draw does, uploaded in the next frame's copy pass.
    internal SampledTexture Create(in DecodedTexture decoded) => new TextureUpload(this, decoded).Finish();

    // Uploads every texture created since the last frame, then spends the frame's upload budget on
    // prefetched pages, in the frame's copy pass. Once the loads settle the decode buffers go, sized by
    // the largest page and needed again only by the next boundary's loads. A discarded decode that
    // returns its buffer late is dropped at the next settled pump.
    internal void Pump(GpuCopyPass copy)
    {
        _copy = copy;
        try
        {
            foreach ((TextureUpload upload, SampledTexture texture) in _deferred)
            {
                upload.Complete(texture.Disposed);
            }

            _deferred.Clear();
            _textures.Pump();
            if (_textures.Settled)
            {
                _pool.Clear();
            }
        }
        finally
        {
            _copy = null;
        }
    }

    // Loads on first use when the scene did not preload the handle.
    internal TextureSlice Get(in TextureHandle handle) =>
        _map.TryGet(handle, out AtlasSlot slot)
            ? new TextureSlice(Get(slot.Page, handle), slot.X, slot.Y)
            : new TextureSlice(Get(handle, handle), 0, 0);

    // The handle's own file, for a material that binds it whole. A packed handle has no file of its
    // own, and its page would bind every other member with it.
    internal SampledTexture GetWhole(in TextureHandle handle)
    {
        if (_map.TryGet(handle, out _))
        {
            throw new InvalidOperationException(
                $"Texture '{handle.Name}' is packed into an atlas, and a material binds its textures whole. Set \"atlas\": false in the {handle.Name[(handle.Name.LastIndexOf('/') + 1)..]}.png.config.json beside it.");
        }

        return Get(handle, handle);
    }

    // The handle's texels inside region as straight RGBA, row by row, decoded from its file or its
    // page. Residency is untouched. A caller outside the frame path can read any handle. Throws
    // when the file is missing or the region falls outside the handle's texels.
    internal byte[] ReadRegion(in TextureHandle handle, TextureRegion region) =>
        ReadRegion(_platform, _map, handle, region);

    internal static byte[] ReadRegion(HostPlatform platform, TextureMap map, in TextureHandle handle, TextureRegion region)
    {
        bool packed = map.TryGet(handle, out AtlasSlot slot);
        TextureHandle file = packed ? slot.Page : handle;

        TexelPool pool = new();
        DecodedTexture decoded;
        using (Stream stream = Open(platform, file))
        {
            decoded = TextureDecoder.Decode(stream, pool, handle.Name, map.Facts(file).SingleChannel);
        }

        // A packed texture's own size bounds the region. Its page holds other textures' texels around it.
        (int width, int height) = packed ? (slot.Width, slot.Height) : (decoded.Width, decoded.Height);
        int left = slot.X + region.X;
        int top = slot.Y + region.Y;
        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0
            || region.X + region.Width > width || region.Y + region.Height > height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                region,
                $"The region falls outside the {width} by {height} texels of '{handle.Name}'. Cut the sprite from inside its texture.");
        }

        byte[] texels = new byte[region.Width * region.Height * 4];
        if (decoded.BytesPerTexel == 1)
        {
            // An r8 texture as a plain draw shows it: white, with the value as its opacity.
            for (int row = 0; row < region.Height; row++)
            {
                for (int column = 0; column < region.Width; column++)
                {
                    int to = ((row * region.Width) + column) * 4;
                    texels[to] = texels[to + 1] = texels[to + 2] = 255;
                    texels[to + 3] = decoded.Texels[((top + row) * decoded.Width) + left + column];
                }
            }

            pool.Return(decoded.Texels);

            return texels;
        }

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

    // The handle's own file, relative to the publish root.
    internal static string RelativePathOf(in TextureHandle handle) => Files.RelativePathOf(handle.Name, handle.Extension);

    internal static Stream Open(HostPlatform platform, in TextureHandle handle) => Files.Open(platform, handle.Name, handle.Extension);

    public void Dispose() => _textures.Dispose();

    private SampledTexture Get(in TextureHandle file, in TextureHandle drawn)
    {
        if (_textures.TryGet(file, out SampledTexture? texture))
        {
            return texture;
        }

        texture = _textures.Load(file);
        Log.Info($"'{drawn.Name}' loaded on first draw. Declare it to preload it");

        return texture;
    }

    // One decoded page on its way to the device, top rows first. Its texels go back to the pool once
    // every row is uploaded, or once the texture is discarded.
    internal sealed class TextureUpload(TextureStore store, DecodedTexture decoded, GpuSampler? sampler = null) : IPendingAsset<SampledTexture>
    {
        private GpuTexture? _texture;
        private int _rows;

        public bool Advance(ref long budget)
        {
            int rowBytes = decoded.Width * decoded.BytesPerTexel;
            int rows = (int)Math.Clamp(budget / rowBytes, 0, decoded.Height - _rows);
            if (rows == 0)
            {
                return false;
            }

            Upload(rows);
            budget -= (long)rows * rowBytes;

            return _rows == decoded.Height;
        }

        // Inside a pump the remaining rows upload now. Outside one they wait for the next.
        public SampledTexture Finish()
        {
            SampledTexture texture = new(_texture ??= Create(), sampler);
            if (store._copy is null)
            {
                store._deferred.Add((this, texture));
            }
            else
            {
                Complete(discarded: false);
            }

            return texture;
        }

        public void Discard()
        {
            _texture?.Dispose();
            _texture = null;
            store._pool.Return(decoded.Texels);
        }

        // Uploads the rows not yet on the device, unless the texture went first, and returns the texels.
        internal void Complete(bool discarded)
        {
            if (!discarded && _rows < decoded.Height)
            {
                Upload(decoded.Height - _rows);
            }

            store._pool.Return(decoded.Texels);
        }

        private GpuTexture Create() =>
            store._device.CreateTexture(new GpuTextureInfo(SampledTexture.FormatOf(decoded), GpuTextureUsage.Sampler, decoded.Width, decoded.Height));

        // Row slices, which cost less than one whole-texture upload of the page.
        private void Upload(int rows)
        {
            _texture ??= Create();
            int rowBytes = decoded.Width * decoded.BytesPerTexel;
            store._copy!.Value.UploadToTexture(_texture, 0, _rows, decoded.Width, rows, decoded.Texels.AsSpan(_rows * rowBytes, rows * rowBytes));
            _rows += rows;
        }
    }
}
