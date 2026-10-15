using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

public sealed class ShaderGlobalsTests
{
    private static readonly Shader Graded = new(
        "effects/graded",
        new ShaderParameter("WorldAdd", ShaderParameterKind.Vector3, Global: true),
        new ShaderParameter("Fade", ShaderParameterKind.Float, Global: true),
        new ShaderParameter("Glow", ShaderParameterKind.Vector4));

    // Resolved from the frame each scene builds, as the host reads it: the scene's own values, whichever
    // material is drawing, and zero for what the scene never set.
    [Fact]
    public void AGlobalSetOnAScene_IsWhatItsFrameCarries_AndAnotherScenesFrameReadsZero()
    {
        SceneFixtures.HookScene lit = new();
        SceneFixtures.HookScene dark = new();
        lit.ShaderGlobals.Set("WorldAdd", new Vector3(0.1f, 0.2f, 0.3f));
        FrameView litFrame = SceneFixtures.Simulation(lit).View;
        FrameView darkFrame = SceneFixtures.Simulation(dark).View;
        GlobalSlots slots = new(Graded);

        ReadOnlySpan<int> inLit = slots.Resolve(litFrame.ShaderGlobals);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0f), litFrame.ShaderGlobals.ValueAt(inLit[0]));
        Assert.Equal(-1, inLit[1]);

        Assert.Equal([-1, -1], slots.Resolve(darkFrame.ShaderGlobals).ToArray());
    }

    [Fact]
    public void ANameSetAfterTheShaderResolved_IsReadOnTheNextResolve()
    {
        ShaderGlobals scene = new();
        GlobalSlots slots = new(Graded);
        Assert.Equal([-1, -1], slots.Resolve(scene).ToArray());

        scene.Set("Fade", 0.5f);

        Assert.Equal(0.5f, scene.ValueAt(slots.Resolve(scene)[1]).X);
    }

    // A rejected scene must not leave the slots half written for the scene resolved before it.
    [Fact]
    public void AResolveThatThrows_LeavesThePreviousScenesValuesIntact()
    {
        ShaderGlobals first = new();
        first.Set("WorldAdd", new Vector3(1f, 2f, 3f));
        first.Set("Fade", 0.5f);
        ShaderGlobals rejected = new();
        rejected.Set("Fade", Vector2.One);
        GlobalSlots slots = new(Graded);
        slots.Resolve(first);

        Assert.Throws<InvalidOperationException>(() => slots.Resolve(rejected));

        ReadOnlySpan<int> again = slots.Resolve(first);
        Assert.Equal(new Vector4(1f, 2f, 3f, 0f), first.ValueAt(again[0]));
        Assert.Equal(0.5f, first.ValueAt(again[1]).X);
    }

    [Fact]
    public void ANameKeepsOneKind_AndDrawingRefusesAShaderThatDeclaresAnother()
    {
        ShaderGlobals scene = new();
        scene.Set("Fade", 1f);

        ArgumentException again = Assert.Throws<ArgumentException>(() => scene.Set("Fade", Vector2.One));
        Assert.StartsWith("Scene global 'Fade' was set as a float and is now set as a float2. Pass it a float every time.", again.Message, StringComparison.Ordinal);

        scene.Set("WorldAdd", 1f);
        InvalidOperationException drawn = Assert.Throws<InvalidOperationException>(() => new GlobalSlots(Graded).Resolve(scene));
        Assert.Equal("Scene global 'WorldAdd' was set as a float, but shader 'effects/graded' declares it as a float3. Set it with a Vector3.", drawn.Message);
    }

    [Fact]
    public void Get_ReadsBackWhatSetStored_AndZeroForANameNeverSet()
    {
        ShaderGlobals scene = new();
        scene.Set("Fade", 0.5f);
        scene.Set("Scroll", new Vector2(1f, 2f));
        scene.Set("WorldAdd", new Vector3(1f, 2f, 3f));
        scene.Set("Glow", new ColorRgba(255, 128, 0, 64));

        Assert.Equal(0.5f, scene.GetFloat("Fade"));
        Assert.Equal(new Vector2(1f, 2f), scene.GetVector2("Scroll"));
        Assert.Equal(new Vector3(1f, 2f, 3f), scene.GetVector3("WorldAdd"));
        Assert.Equal(new ColorRgba(255, 128, 0, 64), scene.GetColor("Glow"));
        Assert.Equal(new Vector4(1f, 128f / 255f, 0f, 64f / 255f), scene.GetVector4("Glow"));
        Assert.Equal(0f, scene.GetFloat("Never"));
        Assert.Equal(default, scene.GetColor("Never"));
    }

    [Fact]
    public void Get_OfAnotherKindThanTheNameWasSetAs_ThrowsNamingTheReader()
    {
        ShaderGlobals scene = new();
        scene.Set("Fade", 1f);

        ArgumentException refused = Assert.Throws<ArgumentException>(() => scene.GetVector3("Fade"));

        Assert.StartsWith("Scene global 'Fade' was set as a float and is read as a float3. Read it with GetFloat.", refused.Message, StringComparison.Ordinal);
    }
}
