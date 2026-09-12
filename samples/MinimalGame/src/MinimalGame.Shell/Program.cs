using Capsule.Rendering;
using Capsule.Runtime.Generated;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

// The render resolution is World.ViewportSize: one canvas pixel is one world pixel, so the font lands
// on the grid the room is drawn on unscaled.
CapsuleBoot.Configure("Minimal Game")
    .WithCommandLine(args)
    .WithInput(GameInput.Configure)
    .WithRenderResolution(320, 180)
    .WithSampling(TextureSampling.Point)
    .RunScene<MainMenu>();
