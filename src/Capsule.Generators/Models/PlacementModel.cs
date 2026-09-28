namespace Capsule.Generators;

/// <summary>One game entry of a shipped document, as its <c>CapsuleGeneratedPlacement</c> attribute carries it.</summary>
/// <param name="Properties">
/// Each authored property with its value: a bool, an int, a double, a string, null, an
/// <see cref="EquatableArray{T}"/> of values, or <see cref="JsonObject"/>.
/// </param>
/// <param name="Line">The line the entry starts on in the document's file, from 1, or 0 when unknown.</param>
/// <param name="Column">The column the entry starts at, from 1, or 0 when unknown.</param>
internal readonly record struct PlacementModel(
    int Id,
    string Type,
    EquatableArray<(string Name, object? Value)> Properties,
    int Line,
    int Column)
{
    /// <summary>What a JSON object arrives as. The build writes one <c>typeof(object)</c>.</summary>
    internal static readonly object JsonObject = new();
}
