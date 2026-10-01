using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

try
{
    // The render resolution matches World.ViewportSize, so one canvas pixel is one world pixel.
    return CapsuleBoot.Configure("Minimal Game", new DesktopPlatform())
        .WithCommandLine(args)
        .WithRunStart(GameBoot.Start)
        .WithRenderResolution(320, 180)
        .WithSampling(TextureSampling.Point)
        .RunScene<MainMenu>();
}
catch (CommandLineException failure)
{
    return failure.Report();
}
