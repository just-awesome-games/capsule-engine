using System.Reflection;
using Capsule.Runtime;
using static Capsule.Tests.Packaging.Surface;

namespace Capsule.Tests.Runtime;

public sealed class PublicApiTests
{
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
