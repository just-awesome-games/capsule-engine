using System.Numerics;
using Capsule.Rendering;
using Capsule.Runtime.Rendering;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class ShaderGlobalsAllocationTests
{
    // What a frame does with globals: the game sets and reads them each step, and each draw resolves them.
    [Fact]
    public void SettingGlobalsAndResolvingThemForADraw_AllocatesNothingOnceWarm()
    {
        Shader graded = new(
            "effects/graded",
            new ShaderParameter("WorldAdd", ShaderParameterKind.Vector3, Global: true),
            new ShaderParameter("Fade", ShaderParameterKind.Float, Global: true));
        ShaderGlobals scene = new();
        GlobalSlots slots = new(graded);
        Material material = new(new Shader("effects/stone", new ShaderParameter("Stain", ShaderParameterKind.Vector3), new ShaderParameter("Glow", ShaderParameterKind.Vector4)));
        float read = 0f;

        for (int frame = 0; frame < 100; frame++)
        {
            Frame(scene, slots, material, frame, ref read);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int frame = 0; frame < 1000; frame++)
        {
            Frame(scene, slots, material, frame, ref read);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(read > 0f);
    }

    private static void Frame(ShaderGlobals scene, GlobalSlots slots, Material material, int frame, ref float read)
    {
        scene.Set("WorldAdd", new Vector3(frame, 0f, 0f));
        scene.Set("Fade", ColorRgba.White.R / 255f);
        scene.Set("Tint", ColorRgba.White);
        material.Set("Stain", new Vector3(frame, 0f, 0f));
        read += material.GetVector3("Stain").X + material.GetColor("Glow").A;
        read += scene.GetVector3("WorldAdd").X + scene.GetFloat("Fade") + scene.GetColor("Tint").R;

        ReadOnlySpan<int> sources = slots.Resolve(scene);
        for (int g = 0; g < sources.Length; g++)
        {
            read += scene.ValueAt(sources[g]).X;
        }
    }
}
