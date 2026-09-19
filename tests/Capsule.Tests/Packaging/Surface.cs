using System.Reflection;

namespace Capsule.Tests.Packaging;

/// <summary>Reflection over an assembly's externally reachable surface.</summary>
internal static class Surface
{
    /// <summary>Every member a type declares itself, at any accessibility.</summary>
    internal const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Whether a consumer of the assembly can reach <paramref name="member"/> itself.</summary>
    internal static bool IsVisibleOutsideTheAssembly(MemberInfo member) => member switch
    {
        Type type => type.IsVisible,
        MethodBase method => IsVisibleOutsideTheAssembly(method),
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        // A property or event is only as visible as its most visible accessor: a private
        // one is an implementation detail the hiding contract permits.
        PropertyInfo property => property.GetAccessors(nonPublic: true).Any(IsVisibleOutsideTheAssembly),
        EventInfo declared => EventAccessors(declared).Any(IsVisibleOutsideTheAssembly),
        _ => false,
    };

    private static bool IsVisibleOutsideTheAssembly(MethodBase accessor) =>
        accessor.IsPublic || accessor.IsFamily || accessor.IsFamilyOrAssembly;

    private static IEnumerable<MethodInfo> EventAccessors(EventInfo declared)
    {
        MethodInfo?[] accessors =
        [
            declared.GetAddMethod(nonPublic: true),
            declared.GetRemoveMethod(nonPublic: true),
            declared.GetRaiseMethod(nonPublic: true),
        ];

        return accessors.OfType<MethodInfo>();
    }
}
