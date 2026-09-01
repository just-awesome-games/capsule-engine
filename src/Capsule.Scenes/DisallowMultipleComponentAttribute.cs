namespace Capsule.Scenes;

/// <summary>
/// Marks a <see cref="Component"/> type an entity may hold at most one of. The slot belongs to the
/// outermost marked type in a component's hierarchy: every subclass of it — marked again or not —
/// counts against that one slot, so an entity holding any of them refuses every other, in
/// whichever order they are offered. <see cref="Entity.Add"/> is where that is enforced.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class DisallowMultipleComponentAttribute : Attribute
{
}
