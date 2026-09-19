using Capsule.Generators;

namespace Capsule.Build;

/// <summary>Turns a registry refusal into the clause a build error states after the key.</summary>
internal static class Refusals
{
    internal static string Because(RegistryRefusal refusal) =>
        refusal.Fault switch
        {
            RegistryFault.NamedAfterItsClass =>
                $"whose '{refusal.Identifier}' would be declared inside a generated class of that name, so name it something else.",
            RegistryFault.NamedAfterAGeneratedMember =>
                $"whose '{refusal.Identifier}' is a name the generated classes take, so name it something else.",
            _ =>
                $"whose '{refusal.Identifier}' is already declared in that directory by \"{refusal.ClaimedBy}\". Two names differing only in their separators are one C# name.",
        };
}
