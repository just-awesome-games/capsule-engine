using System.Numerics;
using Capsule.Runtime.Generated;
using MinimalGame.Game;

CapsuleBoot.Configure("Package Consumer")
    .WithCameraViewport(new Vector2(320f, 180f))
    .WithBindings(ConsumerInput.Bind)
    .RunScene<Room>();
