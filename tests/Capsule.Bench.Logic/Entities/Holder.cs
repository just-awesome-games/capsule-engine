using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Bench.Logic.Entities;

/// <summary>An entity at the origin carrying one renderer: what a rendering workload's whole world is.</summary>
public sealed class Holder : Entity
{
    public Holder(Component renderer)
        : base(Vector2.Zero) =>
        Add(renderer);
}
