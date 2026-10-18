using Capsule.Rendering;
using Capsule.Runtime;
using Capsule.Runtime.Desktop;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

try
{
    // The render resolution is the viewport, and one canvas pixel is one world pixel.
    return CapsuleBoot.Configure("Minimal Game", new DesktopPlatform())
        .WithCommandLine(args)
        .WithRunStart(GameBoot.Start)
        .WithRenderResolution((int)World.ViewportSize.X, (int)World.ViewportSize.Y)
        .WithSampling(TextureSampling.Point)
        .RunScene<MainMenu>();
}
catch (CommandLineException failure)
{
    return failure.Report();
}
