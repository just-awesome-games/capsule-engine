using System.Reflection;
using System.Xml.Linq;
using Capsule.Assets;
using Capsule.Collision;
using Capsule.Runtime;
using Capsule.Scenes;

namespace Capsule.Tests.Packaging;

public sealed class ApiReferenceTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static TheoryData<string> ShippedAssemblies { get; } = new(
        typeof(AssetCollection).Assembly.GetName().Name!,
        typeof(Aabb2D).Assembly.GetName().Name!,
        typeof(Scene).Assembly.GetName().Name!,
        typeof(CapsuleEngine).Assembly.GetName().Name!);

    [Theory]
    [MemberData(nameof(ShippedAssemblies))]
    public void TheShippedDocumentation_ListsOnlyThePublicSurface(string assemblyName)
    {
        Dictionary<string, MemberInfo> surface = Surface(Assembly.Load(assemblyName));
        List<string> hidden = [];

        foreach (string identifier in DocumentedMembers(assemblyName))
        {
            if (!surface.TryGetValue(identifier, out MemberInfo? member))
            {
                // An identifier this builder cannot spell is reported, never assumed public.
                hidden.Add($"{identifier} (unresolved)");
            }
            else if (!IsVisibleOutsideTheAssembly(member))
            {
                hidden.Add(identifier);
            }
        }

        // The identifier is the whole message, and a collection assertion elides a long one.
        Assert.True(hidden.Count == 0, $"{assemblyName}.xml ships documentation a consumer cannot reach:{Environment.NewLine}{string.Join(Environment.NewLine, hidden)}");
    }

    private static IEnumerable<string> DocumentedMembers(string assemblyName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".xml");
        Assert.True(File.Exists(path), $"{assemblyName} ships no API reference beside its assembly.");

        return XDocument.Load(path)
            .Descendants("member")
            .Select(member => (string?)member.Attribute("name"))
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    // Every declared type and member under the identifier the compiler writes for it, so an entry
    // resolves to the one overload it names rather than to any sibling sharing its name.
    private static Dictionary<string, MemberInfo> Surface(Assembly assembly)
    {
        Dictionary<string, MemberInfo> surface = new(StringComparer.Ordinal);

        foreach (Type type in assembly.GetTypes())
        {
            surface["T:" + Signature(type)] = type;

            foreach (MemberInfo member in type.GetMembers(DeclaredMembers))
            {
                if (member is not Type)
                {
                    surface[DocumentationIdentifier(member)] = member;
                }
            }
        }

        return surface;
    }

    private static string DocumentationIdentifier(MemberInfo member)
    {
        // Explicit interface implementations spell the interface with '#'; a constructor is #ctor.
        string name = member.Name switch
        {
            ".ctor" => "#ctor",
            ".cctor" => "#cctor",
            _ => member.Name.Replace('.', '#'),
        };

        string declaring = Signature(member.DeclaringType!) + ".";

        return member switch
        {
            FieldInfo => "F:" + declaring + name,
            EventInfo => "E:" + declaring + name,
            PropertyInfo property => "P:" + declaring + name + Parameters(property.GetIndexParameters()),
            MethodBase method => "M:" + declaring + name + Arity(method) + Parameters(method.GetParameters()) + Conversion(method),
            _ => "?:" + declaring + name,
        };
    }

    private static string Arity(MethodBase method) =>
        method.IsGenericMethodDefinition ? "``" + method.GetGenericArguments().Length : string.Empty;

    private static string Parameters(ParameterInfo[] parameters) =>
        parameters.Length == 0 ? string.Empty : "(" + string.Join(",", parameters.Select(parameter => Signature(parameter.ParameterType))) + ")";

    // A conversion operator is distinguished only by what it returns.
    private static string Conversion(MethodBase method) =>
        method is MethodInfo { Name: "op_Implicit" or "op_Explicit" } conversion ? "~" + Signature(conversion.ReturnType) : string.Empty;

    private static string Signature(Type type)
    {
        if (type.IsByRef)
        {
            return Signature(type.GetElementType()!) + "@";
        }

        if (type.IsPointer)
        {
            return Signature(type.GetElementType()!) + "*";
        }

        if (type.IsArray)
        {
            int rank = type.GetArrayRank();
            return Signature(type.GetElementType()!) + (rank == 1 ? "[]" : "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]");
        }

        // A method's own type parameters take a second backtick; the declaring type's take one.
        if (type.IsGenericParameter)
        {
            return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;
        }

        List<Type> chain = [];
        for (Type? segment = type; segment is not null; segment = segment.DeclaringType)
        {
            chain.Insert(0, segment);
        }

        // An instantiation names its arguments on the segment that introduced them, replacing that
        // segment's arity marker; a segment introducing none keeps its bare name.
        Type[] arguments = type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericArguments() : [];
        List<string> segments = [];
        int assigned = 0;

        foreach (Type segment in chain)
        {
            int introduced = TypeArity(segment) - TypeArity(segment.DeclaringType);
            segments.Add(introduced > 0 && arguments.Length > 0
                ? segment.Name[..segment.Name.LastIndexOf('`')]
                    + "{" + string.Join(",", arguments.Skip(assigned).Take(introduced).Select(Signature)) + "}"
                : segment.Name);
            assigned += introduced;
        }

        return (type.Namespace is null ? string.Empty : type.Namespace + ".") + string.Join(".", segments);
    }

    private static int TypeArity(Type? type) => type is { IsGenericType: true } ? type.GetGenericArguments().Length : 0;

    private static bool IsVisibleOutsideTheAssembly(MemberInfo member)
    {
        if (member is Type type)
        {
            return type.IsVisible;
        }

        if (!member.DeclaringType!.IsVisible)
        {
            return false;
        }

        return member switch
        {
            MethodBase method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly,
            FieldInfo field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly,
            PropertyInfo property => property.GetAccessors(nonPublic: true).Any(IsVisibleOutsideTheAssembly),
            EventInfo declaration => declaration.GetAddMethod(nonPublic: true) is { } add && IsVisibleOutsideTheAssembly(add),
            _ => false,
        };
    }
}
