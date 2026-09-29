namespace Capsule.Generators;

/// <summary>One class a scene document's <c>camera</c> key can name, and the key it claims by convention.</summary>
internal readonly record struct CameraModel(
    string QualifiedName,
    string DisplayName,
    string ContainingNamespace,
    string TypeName,
    bool Concrete,
    bool AccessibleParameterless,
    DeclaredAt At) : IClaimingClass
{
    internal bool Valid => Concrete && AccessibleParameterless;
}
