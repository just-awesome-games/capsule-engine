using Capsule.Rendering;
using Capsule.Runtime.Generated;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

// CapsuleBoot is generated into this project by the CapsuleGameShell role: it knows every scene the
// game declares, so the entry point is the only wiring a shell writes.
CapsuleBoot.Configure("Minimal Game")
    .WithBindings(GameInput.Bind)
    .WithSampling(TextureSampling.Point)
    .RunScene<MainMenu>();
