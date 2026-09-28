using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

// Reads every referenced assembly's registry metadata: the provider a logic assembly declares, and the
// keys it claims.
internal static class BootDescriber
{
    internal static BootModel Describe(Compilation compilation)
    {
        if (compilation.GetTypeByMetadataName(MetadataNames.CapsuleEngine) is null)
        {
            return BootModel.None;
        }

        ImmutableArray<RegistryProviderModel>.Builder providers = ImmutableArray.CreateBuilder<RegistryProviderModel>();
        ImmutableArray<string>.Builder invalid = ImmutableArray.CreateBuilder<string>();
        foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            AttributeData? providerAttribute = null;
            ImmutableArray<RegistryClaimModel>.Builder claims = ImmutableArray.CreateBuilder<RegistryClaimModel>();
            bool malformed = false;

            foreach (AttributeData attribute in assembly.GetAttributes())
            {
                string attributeName = attribute.AttributeClass?.ToDisplayString() ?? string.Empty;
                if (string.Equals(attributeName, MetadataNames.RegistryProviderAttribute, StringComparison.Ordinal))
                {
                    if (providerAttribute is not null)
                    {
                        malformed = true;
                        break;
                    }

                    providerAttribute = attribute;
                }
                else if (string.Equals(attributeName, MetadataNames.RegistryClaimAttribute, StringComparison.Ordinal))
                {
                    if (Claim(attribute) is not { } claim)
                    {
                        malformed = true;
                        break;
                    }

                    claims.Add(claim);
                }
            }

            if (malformed)
            {
                invalid.Add(assembly.Name);
                continue;
            }

            if (providerAttribute is null)
            {
                continue;
            }

            if (providerAttribute.ConstructorArguments.Length != 1
                || providerAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol providerType)
            {
                invalid.Add(assembly.Name);
                continue;
            }

            providers.Add(new RegistryProviderModel(assembly.Name, SymbolShape.QualifiedName(providerType), new(claims.ToImmutable())));
        }

        providers.Sort(static (left, right) => string.CompareOrdinal(left.AssemblyName, right.AssemblyName));

        return new BootModel(true, new(providers.ToImmutable()), new(invalid.ToImmutable()));
    }

    // The claim GeneratedFile.ClaimAttribute wrote, or null for an attribute of any other shape.
    private static RegistryClaimModel? Claim(AttributeData attribute) =>
        attribute.ConstructorArguments is { Length: 3 } arguments
        && arguments[0].Value is int kind and ((int)RegistryClaimKind.Entity or (int)RegistryClaimKind.SceneDocument)
        && arguments[1].Value is string key
        && arguments[2].Value is INamedTypeSymbol declaringType
            ? new RegistryClaimModel((RegistryClaimKind)kind, key, declaringType.ToDisplayString())
            : null;
}
