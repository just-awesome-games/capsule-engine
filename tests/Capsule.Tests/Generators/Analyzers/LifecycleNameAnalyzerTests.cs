using System.Collections.Immutable;
using Capsule.Diagnostics;
using Capsule.Generators;
using Capsule.Physics;
using Capsule.Scenes;
using Microsoft.CodeAnalysis;
using static Capsule.Tests.Generators.GameBoundaryFixtures;

namespace Capsule.Tests.Generators;

public sealed class LifecycleNameAnalyzerTests
{
    private const string Usings = "using System.Numerics; using Capsule.Physics; using Capsule.Scenes;\n";

    private static readonly ImmutableArray<MetadataReference> Engine =
    [
        MetadataReference.CreateFromFile(typeof(Scene).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Collider2D).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Log).Assembly.Location),
    ];

    // A private method nothing references and an override of nothing are dead, and the report names the
    // hooks that do run. A scene's report names a scene's hooks.
    [Theory]
    [InlineData("public sealed class Avatar() : Entity(Vector2.Zero) { private void Update() { } }", "'Update'", "OnAddedToScene")]
    [InlineData("public sealed class Sensor : Component { void OnTriggerEnter2D(Collider2D other) { } }", "'OnTriggerEnter2D'", "ContactEntered")]
    [InlineData("public sealed class North : Scene { public override void _Ready() { } }", "'_Ready'", "OnStop")]
    public async Task ALifecycleNameCapsuleNeverCalls_IsReported(string source, string name, string hook)
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(Usings + source, logic: true, extraReferences: Engine, analyzer: new LifecycleNameAnalyzer());

        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("CAP110", diagnostic.Id);
        Assert.StartsWith(name + " is never called.", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(hook, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    // A private method its type calls is live, from a nested type at any depth or another partial
    // declaration too. A public one is the game's own API, and a type deriving none of the three keeps any
    // name it likes.
    [Theory]
    [InlineData("public sealed class Avatar() : Entity(Vector2.Zero) { private void Start() { } protected override void OnStart() => Start(); }")]
    [InlineData("public sealed class Avatar() : Entity(Vector2.Zero) { public void Start() { } }")]
    [InlineData("public sealed class Avatar() : Entity(Vector2.Zero) { private void Update() { } private static class Helper { public static void Tick(Avatar avatar) => avatar.Update(); } }")]
    [InlineData("public class Avatar() : Entity(Vector2.Zero) { private void Update() { } private sealed class Outer { public sealed class Helper<T> where T : Avatar { public void Tick(T avatar) => avatar.Update(); } } }")]
    [InlineData("public sealed partial class Avatar() : Entity(Vector2.Zero) { private void Update() { } } public sealed partial class Avatar { private void Rest() => Update(); }")]
    [InlineData("public sealed class Plain { private void Update() { } }")]
    public async Task ALiveOrUnrelatedMethodOfALifecycleName_IsSilent(string source)
    {
        ImmutableArray<Diagnostic> diagnostics = await Analyze(Usings + source, logic: true, extraReferences: Engine, analyzer: new LifecycleNameAnalyzer());

        Assert.Empty(diagnostics);
    }
}
