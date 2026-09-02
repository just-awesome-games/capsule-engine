using System.Numerics;
using Capsule.Runtime.Generated;
using MinimalGame.Game;

// CapsuleBoot is generated into this project by the CapsuleGameShell role: it knows every scene the
// game declares, so the entry point is the only wiring a shell writes.
CapsuleBoot.Configure("Minimal Game")
    .WithCameraViewport(new Vector2(320f, 180f))
    .WithBindings(GameInput.Bind)
    .RunScene<MainMenu>();
