using Capsule.Runtime;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

try
{
    // The render resolution is the viewport, and one canvas pixel is one world pixel.
    return CapsuleBoot.Configure("Minimal Game")
        .WithCommandLine(args)
        .WithGame(static run => new MinimalGameInstance(run))
        .WithRenderResolution((int)World.ViewportSize.X, (int)World.ViewportSize.Y)
        .WithPixelPerfect()
        .RunScene<MainMenu>();
}
catch (CommandLineException failure)
{
    return failure.Report();
}
