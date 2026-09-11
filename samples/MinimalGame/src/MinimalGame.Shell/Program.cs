using Capsule.Rendering;
using Capsule.Runtime.Generated;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

// CapsuleBoot is generated into this project by the CapsuleGameShell role: it knows every scene the
// game declares, so the entry point is the only wiring a shell writes.
// The render resolution is the world span the cameras use, so the interface canvas is the same grid
// the room is drawn on: one canvas pixel is one world pixel, and the font lands on it unscaled.
CapsuleBoot.Configure("Minimal Game")
    .WithCommandLine(args)
    .WithInput(GameInput.Configure)
    .WithRenderResolution(320, 180)
    .WithSampling(TextureSampling.Point)
    .RunScene<MainMenu>();
