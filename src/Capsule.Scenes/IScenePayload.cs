namespace Capsule.Scenes;

/// <summary>The type every scene payload implements, the state a transition hands the scene it opens.</summary>
/// <remarks>
/// The build registers each class that implements it, directly or through a base type, when the class has
/// a public parameterless constructor. The development flag <c>--payload</c> names a registered class by its
/// class name.
/// </remarks>
/// <example>
/// <code>
/// public record Arrival(string Entrance) : IScenePayload;
///
/// public sealed record WestArrival() : Arrival("west");
/// </code>
/// The command line <c>--scene Cave --payload WestArrival</c> then boots <c>Cave</c> with a new
/// <c>WestArrival</c> as its <c>EntryPayload</c>.
/// </example>
public interface IScenePayload;
