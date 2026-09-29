namespace Capsule.Physics;

/// <summary>What a <see cref="KinematicBody2D"/> calls a surface, judged by its normal against the body's <see cref="KinematicBody2D.MaxFloorAngle"/>.</summary>
public enum SurfaceKind
{
    /// <summary>A surface whose normal is within <see cref="KinematicBody2D.MaxFloorAngle"/> of up.</summary>
    Floor,

    /// <summary>A surface that is neither a floor nor a ceiling.</summary>
    Wall,

    /// <summary>A surface whose normal is within <see cref="KinematicBody2D.MaxFloorAngle"/> of down.</summary>
    Ceiling,
}
