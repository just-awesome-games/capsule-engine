namespace Capsule.Generators;

/// <summary>The <c>CapsuleAssets</c> members one declaration of a scene, entity or component class reads in its own code.</summary>
/// <remarks>Each part of a partial class is a model of its own, and the resolver joins them.</remarks>
/// <param name="TypeOfName">The class as <c>typeof</c> names it, unbound where it is generic.</param>
/// <param name="Reads">Each member read, fully qualified, once each in source order.</param>
internal readonly record struct PreloadModel(string TypeOfName, EquatableArray<string> Reads);
