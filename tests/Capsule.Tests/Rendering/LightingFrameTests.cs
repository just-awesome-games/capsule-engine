using System.Numerics;
using Capsule.Build.Shaders;
using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Gpu;
using Capsule.Runtime.Platform;
using Capsule.Runtime.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Build;
using Capsule.Tests.Runtime;
using Capsule.Tests.Scenes;
using Capsule.UI;

namespace Capsule.Tests.Rendering;

/// <summary>
/// The light as a GPU draws it, read back from a captured frame. The suite does not assume a GPU, and on a machine
/// with none every test here is skipped.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class LightingFrameTests : IDisposable
{
    private const int Width = 64;
    private const int Height = 32;

    // The fragment reads the light and nothing else, drawn by an unlit entity as a shader lighting itself would be.
    private const string ReadsLight = "float4 Fragment(SpritePixel pixel) { return float4(SampleLight().rgb, 1.0); }";

    private static readonly ColorRgba Dark = new(40, 40, 40);

    private readonly TempWorkspace _workspace = new(nameof(LightingFrameTests));

    public void Dispose() => _workspace.Dispose();

    // A lit sprite is its colour times the light at its pixel: the ambient, and a light added over it with no
    // ceiling at two. An unlit entity's sprite, an additive sprite's surroundings and the screen layer keep their
    // own colour, and an additive sprite lights nothing.
    [Fact]
    public void ALitFrame_MultipliesLitWorldSpritesByTheLight_AndNothingElse()
    {
        SceneFixtures.HookScene scene = Framed();

        // A flat light of 3 over the left quarter, past any ceiling at two.
        scene.Add(Holding(new Vector2(0f, 0f), new PolygonLight([Vector2.Zero, new(16f, 0f), new(16f, 32f), new(0f, 32f)]) { Intensity = 3f }));

        // An additive grey square, which brightens itself and nothing beside it.
        scene.Add(Holding(new Vector2(24f, 4f), new ColorRect(new Vector2(4f, 4f)) { Color = new ColorRgba(50, 50, 50), Blend = BlendMode.Additive }));

        EntityHierarchyFixtures.Node unlit = new(new Vector2(40f, 4f)) { Unlit = true };
        unlit.Add(new ColorRect(new Vector2(4f, 4f)) { Color = new ColorRgba(100, 100, 100) });
        scene.Add(unlit);

        ScreenEntity screen = new(Anchor.TopLeft, new Vector2(56f, 4f));
        screen.Add(new ColorRect(new Vector2(4f, 4f)) { Color = new ColorRgba(100, 100, 100) });
        scene.Add(screen);

        byte[] frame = Draw(scene, ambient: new ColorRgba(128, 128, 128));

        Near(140, Red(frame, 8, 16));
        Near(20, Red(frame, 20, 16));
        Near(45, Red(frame, 25, 5));
        Near(20, Red(frame, 30, 5));
        Near(100, Red(frame, 41, 5));
        Near(100, Red(frame, 57, 5));
    }

    // A game shader is lit and unlit as the engine's sprite is: multiplied by the light on a lit entity, and at its
    // own colour beneath an unlit one in the same lit frame.
    [Fact]
    public void AGameShader_IsLit_AndKeepsItsColourBeneathAnUnlitEntity()
    {
        Material flat = new(Compile("lighting/flat", "float4 Fragment(SpritePixel pixel) { return pixel.Texel * pixel.Tint; }"));
        SceneFixtures.HookScene scene = Framed();
        scene.Add(Holding(new Vector2(8f, 8f), new ColorRect(new Vector2(4f, 4f)) { Color = new ColorRgba(200, 200, 200), Material = flat }));
        EntityHierarchyFixtures.Node unlit = new(new Vector2(24f, 8f)) { Unlit = true };
        unlit.Add(new ColorRect(new Vector2(4f, 4f)) { Color = new ColorRgba(200, 200, 200), Material = flat });
        scene.Add(unlit);

        byte[] frame = Draw(scene, ambient: new ColorRgba(128, 128, 128));

        Near(100, Red(frame, 9, 9));
        Near(200, Red(frame, 25, 9));
    }

    // A light follows its entity's scale on each axis, and a negative one mirrors it. The white texel, pivoted at its
    // corner, lights the rect on the side the scale turns it to.
    [Fact]
    public void ALight_ScaledNegativeOnAnAxis_LightsTheMirroredSide()
    {
        SceneFixtures.HookScene scene = Framed();
        EntityHierarchyFixtures.Node lamp = new(new Vector2(32f, 8f)) { Scale = new Vector2(-1f, 0.5f) };
        lamp.Add(new PointLight { Sprite = Sprite.White, Radius = 4f });
        scene.Add(lamp);

        byte[] frame = Draw(scene, ambient: ColorRgba.Black);

        Near(40, Red(frame, 28, 9));
        Near(0, Red(frame, 35, 9));
        Near(0, Red(frame, 28, 13));
    }

    // SampleLight reads the light at its pixel: the ambient in a lit frame, and white in a frame that lights nothing
    // and on the screen layer.
    [Theory]
    [InlineData(true, 128, 255)]
    [InlineData(false, 255, 255)]
    public void SampleLight_ReadsTheLightAtItsPixel_AndWhiteWhereNothingLights(bool lit, int world, int onScreen)
    {
        Shader shader = Compile("lighting/reads", ReadsLight);
        SceneFixtures.HookScene scene = Framed();
        EntityHierarchyFixtures.Node unlit = new(new Vector2(8f, 8f)) { Unlit = true };
        unlit.Add(new ColorRect(new Vector2(4f, 4f)) { Material = new Material(shader) });
        scene.Add(unlit);
        ScreenEntity screen = new(Anchor.TopLeft, new Vector2(40f, 8f));
        screen.Add(new ColorRect(new Vector2(4f, 4f)) { Material = new Material(shader) });
        scene.Add(screen);

        byte[] frame = Draw(scene, ambient: lit ? new ColorRgba(128, 128, 128) : ColorRgba.White);

        Near(world, Red(frame, 9, 9));
        Near(onScreen, Red(frame, 41, 9));
    }

    // An unlit scene draws as if it were the unlit root of every entity. Under a light of a quarter over a quarter
    // ambient, a plain sprite and the clear colour keep their own colour, and SampleLight still reads the half.
    [Fact]
    public void AnUnlitScene_KeepsEveryDrawsColourUnderALight_AndSampleLightStillReadsIt()
    {
        Shader shader = Compile("lighting/reads", ReadsLight);
        SceneFixtures.HookScene scene = Framed();
        scene.Add(Holding(Vector2.Zero, new PolygonLight([Vector2.Zero, new(32f, 0f), new(32f, 32f), new(0f, 32f)]) { Intensity = 0.25f }));
        scene.Add(Holding(new Vector2(8f, 8f), new ColorRect(new Vector2(4f, 4f)) { Color = new ColorRgba(100, 100, 100) }));
        scene.Add(Holding(new Vector2(24f, 8f), new ColorRect(new Vector2(4f, 4f)) { Material = new Material(shader) }));

        byte[] frame = Draw(scene, ambient: new ColorRgba(64, 64, 64), unlit: true);

        Near(100, Red(frame, 9, 9));
        Near(40, Red(frame, 4, 24));
        Near(128, Red(frame, 25, 9));
    }

    // The default light falls off as one minus the squared distance. A pixel half its radius out reads about three
    // quarters of it, where a squared falloff would read a quarter.
    [Fact]
    public void TheDefaultLight_HalfItsRadiusOut_ReadsAboutThreeQuarters()
    {
        Shader shader = Compile("lighting/reads", ReadsLight);
        SceneFixtures.HookScene scene = Framed();
        scene.Add(Holding(new Vector2(32f, 16f), new PointLight { Radius = 16f }));
        scene.Add(Holding(new Vector2(40f, 16f), new ColorRect(new Vector2(1f, 1f)) { Material = new Material(shader) }));

        byte[] frame = Draw(scene, ambient: ColorRgba.Black, unlit: true);

        // The pixel's centre sits at (40.5, 16.5), 0.53 of the radius out, where the light is 0.72.
        Assert.InRange(Red(frame, 40, 16), 175, 192);
    }

    private static SceneFixtures.HookScene Framed() =>
        new(start: static scene => SceneFixtures.Open(scene, new Vector2(Width / 2f, Height / 2f), new Vector2(Width, Height)));

    private static EntityHierarchyFixtures.Node Holding(Vector2 position, Component held)
    {
        EntityHierarchyFixtures.Node node = new(position);
        node.Add(held);

        return node;
    }

    // The red channel of a texel, which every colour here holds equal to green and blue.
    private static int Red(byte[] frame, int x, int y) => frame[((y * Width) + x) * 4];

    // Within the rounding a float light and an 8-bit surface leave.
    private static void Near(int expected, int actual) => Assert.InRange(actual, expected - 2, expected + 2);

    private Shader Compile(string name, string fragment)
    {
        ShaderCompilation compiled = ShaderCompiler.CompileFragment(ToolWorkspace.ShaderTools, _workspace.PathTo("compile/fragment"), fragment, "fragment.fx");
        Shader shader = new(name);
        foreach (ShaderContainer container in Assert.IsType<ShaderContainer[]>(compiled.Shaders))
        {
            string path = Path.Combine(_workspace.Root, ShaderStore.RelativePathOf(shader, container.Format));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, container.Write());
        }

        return shader;
    }

    // The scene's frame at the end of its first step, drawn on a GPU with no window at the canvas's size, as
    // premultiplied RGBA rows.
    private byte[] Draw(SceneFixtures.HookScene scene, ColorRgba ambient, bool unlit = false)
    {
        Assert.SkipUnless(Sdl3.SDL_InitSubSystem(Sdl3.InitVideo), "SDL's video subsystem did not start.");
        try
        {
            GpuDevice gpu;
            try
            {
                gpu = new GpuDevice(driver: null);
            }
            catch (InvalidOperationException)
            {
                Assert.Skip("No GPU device opens on this machine.");
                throw;
            }

            using (gpu)
            {
                scene.Light(Dark, ambient, unlit);
                Run run = new() { RenderResolution = (Width, Height), Canvas = new Vector2(Width, Height) };
                using SceneSimulation simulation = new(scene, run: run);
                simulation.Step(SceneFixtures.Step());

                TextureSamplers samplers = new(gpu);
                ContentPlatform platform = new(_workspace.Root);
                using TextureStore textures = new(gpu, samplers, platform);
                using ShaderStore shaders = new(gpu, platform, gpu.SwapchainFormat);
                using FrameRenderer renderer = new(gpu, samplers, (Width, Height), textures, shaders) { OffscreenExtent = (Width, Height) };
                string path = Path.Combine(_workspace.Root, "frame.png");
                renderer.Draw(simulation.View, alpha: 1f, path);
                renderer.Submit();

                using FileStream file = File.OpenRead(path);
                using DecodedTexture decoded = TextureDecoder.Decode(file, "frame");
                Assert.Equal((Width, Height), (decoded.Width, decoded.Height));

                return decoded.Texels.ToArray();
            }
        }
        finally
        {
            Sdl3.SDL_QuitSubSystem(Sdl3.InitVideo);
        }
    }
}
