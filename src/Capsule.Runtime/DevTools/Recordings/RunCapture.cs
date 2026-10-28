using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;
using Capsule.Runtime.Platform;
using Capsule.Runtime.Rendering;
using Capsule.Runtime.Scenes;

namespace Capsule.Runtime.DevTools.Recordings;

// What --capture writes from a headless run, replayed or driven: the frame the game's camera draws of each
// named tick's settled state, uninterpolated, as <directory>/<tick>.png. It is the surface a windowed
// capture saves, laid out at the output extent the run's steps saw, else at the shell's window size, and
// it never shows the overlay. It draws on a GPU device with no window, and a tick outside the selection
// draws nothing.
internal sealed class RunCapture : IDisposable
{
    // The ticks a capture writes before the run halts, unless --force lifts it: ten seconds at 60 steps a
    // second.
    internal const int Ceiling = 600;

    private readonly string _directory;
    private readonly TickSelection _ticks;
    private readonly GpuDevice _gpu;
    private readonly TextureStore _textures;
    private readonly ShaderStore _shaders;
    private readonly FrameRenderer _renderer;
    private SceneHost? _scenes;

    // Throws IOException or UnauthorizedAccessException when directory cannot be created or written, and
    // InvalidOperationException when no GPU device opens. A failure releases whatever it had opened.
    internal RunCapture(EngineBuilder builder, string directory, TickSelection ticks)
    {
        Directory.CreateDirectory(directory);

        // A file this creates and deletes on close proves the directory takes the frames before the run.
        using (new FileStream(Path.Combine(directory, Path.GetRandomFileName() + FrameCapture.TemporarySuffix), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
        {
        }

        _directory = directory;
        _ticks = ticks;

        if (!Sdl3.SDL_InitSubSystem(Sdl3.InitVideo))
        {
            throw new InvalidOperationException($"SDL's video subsystem did not start: {Sdl3.Error()}.");
        }

        GpuDevice? gpu = null;
        TextureStore? textures = null;
        ShaderStore? shaders = null;
        try
        {
            gpu = new GpuDevice(builder.GpuDriver);
            TextureSamplers samplers = new(gpu);
            textures = new TextureStore(gpu, samplers, builder.Platform);
            shaders = new ShaderStore(gpu, builder.Platform, gpu.SwapchainFormat);
            _renderer = new FrameRenderer(gpu, samplers, builder.RenderResolution, textures, shaders);
        }
        catch
        {
            shaders?.Dispose();
            textures?.Dispose();
            gpu?.Dispose();
            Sdl3.SDL_QuitSubSystem(Sdl3.InitVideo);
            throw;
        }

        (_gpu, _textures, _shaders) = (gpu, textures, shaders);
    }

    internal TickSelection Ticks => _ticks;

    // Loads each scene's preloads as a window does at its boundaries, the first scene's included. The run's
    // log is installed by now, and names the device.
    internal void Attach(SceneHost scenes)
    {
        Log.Info($"GPU: {_gpu.Description}");
        _scenes = scenes;
        scenes.PrepareAssets = PrepareAssets;
        scenes.PrepareInitialAssets();
    }

    // Draws and saves the frame of scenes' current scene when tick is one the capture names. output is
    // the extent the step saw, zero when the host gave it none.
    internal void Write(SceneHost scenes, long tick, Vector2 output)
    {
        if (!_ticks.Take(tick))
        {
            return;
        }

        Vector2 extent = output.X > 0f && output.Y > 0f ? output : scenes.Run.Display.WindowSize;
        _renderer.OffscreenExtent = ((int)extent.X, (int)extent.Y);
        string path = Path.Combine(_directory, string.Create(CultureInfo.InvariantCulture, $"{tick:D6}.png"));
        _renderer.Draw(scenes.View, alpha: 1f, path);
        _renderer.Submit();
    }

    // Warns of ticks past the run's end and says what was written. finalTick is the run's last tick with a
    // scene, the one before its exiting step when it exited.
    internal void Finish(long finalTick)
    {
        _ticks.WarnPastEnd(finalTick);
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"--capture wrote {_renderer.CapturesSaved} frames to {_directory}"));
    }

    // A boundary the scenes reach after this would load into the disposed stores.
    public void Dispose()
    {
        if (_scenes is { } scenes)
        {
            scenes.PrepareAssets = null;
        }

        _renderer.Dispose();
        _shaders.Dispose();
        _textures.Dispose();
        _gpu.Dispose();
        Sdl3.SDL_QuitSubSystem(Sdl3.InitVideo);
    }

    private void PrepareAssets(AssetCollection preloads)
    {
        _textures.ChangeScene(preloads, static () => { });
        _shaders.Load(preloads.Shaders);
    }
}
