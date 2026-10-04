namespace Capsule.Generators;

/// <summary>A class a type key names, by the key its namespace and name spell or its <c>[TypeKey]</c> declares.</summary>
internal interface IClaimingClass
{
    string QualifiedName { get; }

    string DisplayName { get; }

    string ContainingNamespace { get; }

    string TypeName { get; }

    string? Declared { get; }

    DeclaredAt At { get; }
}
