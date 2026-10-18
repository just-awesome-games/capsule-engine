using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>2 000 sprites drawn through eight materials of one shader in turn, each binding a texture and a value of its own: a material change, and the shader apply it costs, on every sprite.</summary>
[Workload(WorkloadKind.Rendering)]
public sealed class Materials2k : Scene
{
    private const int Count = 2_000;

    private const int Columns = 50;

    public Materials2k()
    {
        Camera = new ParkedCamera();

        Material[] materials = new Material[8];
        for (int index = 0; index < materials.Length; index++)
        {
            materials[index] = new Material(CapsuleAssets.Shaders.ShadeShader);
            materials[index].Set("Ramp", CapsuleAssets.Textures.StripTexture);
            materials[index].Set("Row", index * 16f);
        }

        Vector2 cell = new(World.ViewportSize.X / Columns, World.ViewportSize.Y / (Count / Columns));
        for (int index = 0; index < Count; index++)
        {
            Add(new Swatch(new Vector2((index % Columns) * cell.X, (index / Columns) * cell.Y), materials[index % materials.Length]));
        }
    }
}
