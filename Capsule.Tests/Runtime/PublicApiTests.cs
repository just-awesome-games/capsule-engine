using System.Reflection;
using Capsule.Runtime;

namespace Capsule.Tests.Runtime;

/// <summary>
/// Guards the MonoGame-hiding contract from the inside. The csproj's PrivateAssets
/// stops a game compiling against MonoGame; this stops the engine handing a MonoGame
/// type out through its own API, which no project setting can catch.
/// The walk is deliberately exhaustive — base types, interfaces, generic constraints,
/// every externally visible nested type, and each member's parameters, return, field,
/// property and event types, following generic arguments at every one of those
/// positions — because a leak only has to find one position the guard skipped.
/// </summary>
public sealed class PublicApiTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void NoMonoGameType_ReachesTheRuntimesPublicSurface()
    {
        List<string> leaks = [];

        foreach (Type type in ExternallyVisibleTypes(typeof(CapsuleEngine).Assembly))
        {
            Inspect(leaks, type, type.BaseType);

            foreach (Type contract in type.GetInterfaces())
            {
                Inspect(leaks, type, contract);
            }

            InspectConstraints(leaks, type, type.GetGenericArguments());

            foreach (MemberInfo member in type.GetMembers(DeclaredMembers))
            {
                if (!IsVisibleOutsideTheAssembly(member))
                {
                    continue;
                }

                foreach (Type signature in SignatureTypes(member))
                {
                    Inspect(leaks, member, signature);
                }

                if (member is MethodBase { IsGenericMethodDefinition: true } method)
                {
                    InspectConstraints(leaks, member, method.GetGenericArguments());
                }
            }
        }

        Assert.Empty(leaks);
    }

    private static IEnumerable<Type> ExternallyVisibleTypes(Assembly assembly)
    {
        foreach (Type type in assembly.GetExportedTypes())
        {
            // GetExportedTypes already flattens public nested types; recursing from the
            // top level instead reaches the protected ones it leaves out, without repeats.
            if (type.IsNested)
            {
                continue;
            }

            foreach (Type reachable in WithNestedTypes(type))
            {
                yield return reachable;
            }
        }
    }

    private static IEnumerable<Type> WithNestedTypes(Type type)
    {
        yield return type;

        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!nested.IsNestedPublic && !nested.IsNestedFamily && !nested.IsNestedFamORAssem)
            {
                continue;
            }

            foreach (Type reachable in WithNestedTypes(nested))
            {
                yield return reachable;
            }
        }
    }

    private static void InspectConstraints(List<string> leaks, object site, Type[] typeParameters)
    {
        foreach (Type parameter in typeParameters)
        {
            foreach (Type constraint in parameter.GetGenericParameterConstraints())
            {
                Inspect(leaks, site, constraint);
            }
        }
    }

    private static void Inspect(List<string> leaks, object site, Type? type)
    {
        if (type is null)
        {
            return;
        }

        Type root = type.HasElementType ? type.GetElementType()! : type;

        foreach (Type argument in root.GetGenericArguments())
        {
            Inspect(leaks, site, argument);
        }

        if (root.Assembly.GetName().Name?.StartsWith("MonoGame", StringComparison.Ordinal) == true)
        {
            leaks.Add($"{site} exposes {root.FullName}");
        }
    }

    private static bool IsVisibleOutsideTheAssembly(MemberInfo member) => member switch
    {
        MethodBase method => IsVisibleOutsideTheAssembly(method),
        FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
        // A property or event is only as visible as its most visible accessor: a private
        // one is an implementation detail the hiding contract permits.
        PropertyInfo property => property.GetAccessors(nonPublic: true).Any(IsVisibleOutsideTheAssembly),
        EventInfo declared => EventAccessors(declared).Any(IsVisibleOutsideTheAssembly),
        // Nested types are walked as types, so as members they are already covered.
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

    private static IEnumerable<Type> SignatureTypes(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method:
                yield return method.ReturnType;
                break;
            case FieldInfo field:
                yield return field.FieldType;
                break;
            case PropertyInfo property:
                yield return property.PropertyType;
                break;
            case EventInfo { EventHandlerType: { } handler }:
                yield return handler;
                break;
            default:
                break;
        }

        if (member is MethodBase parameterized)
        {
            foreach (ParameterInfo parameter in parameterized.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
