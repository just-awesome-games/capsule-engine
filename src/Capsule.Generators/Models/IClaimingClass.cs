namespace Capsule.Generators;

/// <summary>A class that claims the key its namespace and name spell, with no attribute to override it: a subclass a type key names.</summary>
internal interface IClaimingClass
{
    string QualifiedName { get; }

    string DisplayName { get; }

    string ContainingNamespace { get; }

    string TypeName { get; }

    DeclaredAt At { get; }
}
