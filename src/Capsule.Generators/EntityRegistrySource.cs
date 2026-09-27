using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal static class EntityRegistrySource
{
    private const string FileName = "CapsuleEntities.g.cs";

    // Entity keys drop this namespace segment, since it repeats the domain.
    private const string DomainSegment = "Entities";

    // The generated registration's own indent, and its arguments' inside it.
    private const string Indent = "                ";

    private const string Body = Indent + "    ";

    internal static EntityModel? Describe(INamedTypeSymbol type, TypeDeclarationSyntax declaration, SemanticModel model)
    {
        Compilation compilation = model.Compilation;
        bool concreteEntity = Symbols.IsConcreteClass(type) && Symbols.DerivesFrom(type, compilation, Symbols.Entity);
        List<IMethodSymbol> constructors = concreteEntity ? Symbols.PublicConstructorsTaking(type, compilation, Symbols.EntitySpawn) : [];
        AttributeData? annotation = Symbols.Attribute(type, compilation, Symbols.SpawnTypeAttribute);

        if (annotation is null)
        {
            // A class of the wrong shape that claims nothing is an ordinary class, not a mistake.
            if (constructors.Count == 0)
            {
                return null;
            }

            EntityFault discoveredFault = constructors.Count > 1
                ? EntityFault.AmbiguousSpawnConstructors
                : Symbols.IsAccessibleFromGeneratedCode(type)
                    ? EntityFault.None
                    : EntityFault.InaccessibleType;

            return SpawnChecked(type, declaration, null, discoveredFault, model, constructors);
        }

        // The attribute has a single form. Any other call is the compiler's error to report.
        if (annotation.ConstructorArguments.Length != 1)
        {
            return null;
        }

        string? spawnType = annotation.ConstructorArguments[0].Value as string;
        if (string.IsNullOrWhiteSpace(spawnType))
        {
            return Model(type, declaration, null, EntityFault.BlankSpawnType);
        }

        EntityFault fault = EntityFault.None;
        if (!concreteEntity)
        {
            fault = EntityFault.NotAConcreteEntity;
        }
        else if (constructors.Count == 0)
        {
            fault = EntityFault.MissingSpawnConstructor;
        }
        else if (constructors.Count > 1)
        {
            fault = EntityFault.AmbiguousSpawnConstructors;
        }
        else if (!Symbols.IsAccessibleFromGeneratedCode(type))
        {
            fault = EntityFault.InaccessibleType;
        }

        return SpawnChecked(type, declaration, spawnType!, fault, model, constructors);
    }

    // A sound claim must also pass its spawn to the base constructor, or the authored band and
    // factor the spawn carries never reach the entity. The fault is reported at the constructor.
    private static EntityModel SpawnChecked(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        string? declared,
        EntityFault fault,
        SemanticModel model,
        List<IMethodSymbol> constructors)
    {
        if (fault != EntityFault.None)
        {
            return Model(type, declaration, declared, fault);
        }

        return Symbols.PassesSpawnOn(constructors[0], model, out Location? at)
            ? Model(type, declaration, declared, fault, properties: PropertySchema.Of(type, model.Compilation), spawn: constructors[0].Parameters[0].RefKind)
            : Model(type, declaration, declared, EntityFault.SpawnNotPassedToBase, at);
    }

    internal static void Emit(
        SourceProductionContext context,
        ImmutableArray<EntityModel> models,
        bool enginePresent,
        string rootNamespace,
        ImmutableArray<SceneDocumentInfo> documents,
        AssetTable assets)
    {
        if (!enginePresent)
        {
            return;
        }

        List<Registration> sound = [];
        RegistryPass.Sound(
            context,
            models,
            static model => model.QualifiedName,
            static model => model.DisplayName,
            static model => model.At,
            static model => Reported(model.Fault),
            model => Resolve(context, sound, model, rootNamespace));

        List<Registration> registered = RegistryPass.Claimed(
            context,
            sound,
            static (left, right) =>
            {
                int byType = string.CompareOrdinal(left.SpawnType, right.SpawnType);

                return byType != 0 ? byType : string.CompareOrdinal(left.Model.QualifiedName, right.Model.QualifiedName);
            },
            static entry => entry.SpawnType,
            static entry => entry.Model.DisplayName,
            static entry => entry.Model.At,
            RegistryDiagnostics.DuplicateSpawnType);

        PlacementCheck.Run(
            context,
            documents,
            registered.ToDictionary(static entry => entry.SpawnType, static entry => entry.Model, StringComparer.Ordinal),
            new HashSet<string>(models.Select(model => KeyOf(model, rootNamespace)), StringComparer.Ordinal),
            assets);

        registered.RemoveAll(static entry => entry.Model.CodeOnly);
        context.AddSource(FileName, SourceText.From(Render(registered, assets), Encoding.UTF8));
    }

    private static DiagnosticDescriptor? Reported(EntityFault fault) => fault switch
    {
        EntityFault.NotAConcreteEntity => RegistryDiagnostics.NotAConcreteEntity,
        EntityFault.MissingSpawnConstructor => RegistryDiagnostics.MissingSpawnConstructor,
        EntityFault.BlankSpawnType => RegistryDiagnostics.BlankSpawnType,
        EntityFault.InaccessibleType => RegistryDiagnostics.InaccessibleRegisteredType,
        EntityFault.AmbiguousSpawnConstructors => RegistryDiagnostics.AmbiguousEntityConstructors,
        EntityFault.SpawnNotPassedToBase => RegistryDiagnostics.SpawnNotPassedToBase,
        _ => null,
    };

    // The key comes from where the type is declared, so it is not settled until the assembly's root
    // namespace is known. An explicit [SpawnType] names the full key under the same grammar.
    private static void Resolve(
        SourceProductionContext context,
        List<Registration> sound,
        EntityModel model,
        string rootNamespace)
    {
        string spawnType = KeyOf(model, rootNamespace);

        if (AssetPaths.IsKey(spawnType))
        {
            sound.Add(new Registration(spawnType, model));

            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RegistryDiagnostics.UnsafeSpawnType, model.At.Location(), model.DisplayName, spawnType));
    }

    private static string KeyOf(EntityModel model, string rootNamespace) =>
        model.Declared ?? TypeNaming.KeyFor(model.ContainingNamespace, model.TypeName, rootNamespace, DomainSegment);

    private static EntityModel Model(
        INamedTypeSymbol type,
        TypeDeclarationSyntax declaration,
        string? declared,
        EntityFault fault,
        Location? at = null,
        EquatableArray<PropertyModel> properties = default,
        RefKind spawn = RefKind.None) =>
        new(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ToDisplayString(),
            type.ContainingNamespace is { IsGlobalNamespace: false } space ? space.ToDisplayString() : string.Empty,
            type.Name,
            declared,
            fault,
            DeclaredAt.From(at ?? declaration.Identifier.GetLocation()),
            properties,
            PropertySchema.AssignableTo(type),
            spawn switch
            {
                RefKind.In => "in ",
                RefKind.RefReadOnlyParameter => "ref readonly ",
                _ => string.Empty,
            });

    private static string Render(List<Registration> registered, AssetTable assets)
    {
        StringBuilder claims = new();
        StringBuilder registrations = new();

        foreach (Registration entry in registered)
        {
            claims.Append("[assembly: global::Capsule.Generated.CapsuleGeneratedRegistryClaimAttribute(0, ")
                .Append(Literal(entry.SpawnType))
                .Append(", typeof(").Append(entry.Model.QualifiedName).AppendLine("))]");

            string spawner = entry.Model.Required
                ? $"static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => {ConstructorName(entry.Model)}(spawn)"
                : $"static (global::Capsule.Scenes.Spawning.EntitySpawn spawn) => new {entry.Model.QualifiedName}(spawn)";
            registrations.Append(Indent).Append("new global::Capsule.Scenes.Spawning.EntityRegistration(");
            bool applies = Authored(entry.Model).Any(static property => property.Kind != PropertyKind.Reference);
            bool links = Authored(entry.Model).Any(static property => property.Kind == PropertyKind.Reference);
            if (applies || links)
            {
                registrations.Append('\n')
                    .Append(Body).Append(Literal(entry.SpawnType)).Append(",\n")
                    .Append(Body).Append(spawner);
                if (applies)
                {
                    registrations.Append(",\n").Append(Body).Append(Applier(entry.Model, references: false));
                }

                if (links)
                {
                    registrations.Append(",\n").Append(Body).Append("Link: ").Append(Applier(entry.Model, references: true));
                }

                registrations.AppendLine("),");
            }
            else
            {
                registrations.Append(Literal(entry.SpawnType)).Append(", ").Append(spawner).AppendLine("),");
            }
        }

        if (registered.Count > 0)
        {
            claims.AppendLine();
        }

        string accessors = Constructors(registered.Select(static entry => entry.Model).Where(static model => model.Required))
            + Accessors(registered.SelectMany(static entry => Authored(entry.Model)).Where(static property => !property.Direct))
            + Lookups(registered.SelectMany(static entry => Authored(entry.Model)), assets);

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{claims}}namespace Capsule.Generated
            {
                /// <summary>Every spawnable entity this assembly declares, as one registry. Generated code. Do not edit.</summary>
                [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                public static class CapsuleEntities
                {
                    internal static global::Capsule.Scenes.Spawning.EntityRegistration[] Registrations { get; } =
                        new global::Capsule.Scenes.Spawning.EntityRegistration[]
                        {
            {{registrations}}            };

                    /// <summary>The registry a scene resolves its spawn types through.</summary>
                    public static global::Capsule.Scenes.Spawning.EntityRegistry Registry { get; } =
                        new global::Capsule.Scenes.Spawning.EntityRegistry(Registrations);
            {{accessors}}    }
            }

            """;
    }

    private static IEnumerable<PropertyModel> Authored(EntityModel model) =>
        model.Properties.Items.Where(static property => property.Authorable && property.Settable);

    // Sets each required member, and each optional one the entry authors. The base constructor calls the
    // applier of every other member before the derived body runs. The scene calls the applier of references
    // once every entry of the document is constructed.
    private static string Applier(EntityModel model, bool references)
    {
        string indent = Body + "    ";
        StringBuilder applier = new StringBuilder("static (placed, properties) =>\n")
            .Append(Body).Append("{\n")
            .Append(indent).Append(model.QualifiedName).Append(" entity = (").Append(model.QualifiedName).Append(")placed;\n");
        foreach (PropertyModel property in Authored(model).Where(property => (property.Kind == PropertyKind.Reference) == references))
        {
            if (property.Required)
            {
                applier.Append(indent).Append(Assignment(property, indent)).Append('\n');
                continue;
            }

            applier.Append(indent).Append("if (properties.Has(").Append(Literal(property.Key)).Append("))\n")
                .Append(indent).Append("{\n")
                .Append(indent).Append("    ").Append(Assignment(property, indent + "    ")).Append('\n')
                .Append(indent).Append("}\n");
        }

        return applier.Append(Body).Append('}').ToString();
    }

    // Plain C# where the game can assign the member, and an accessor where it cannot.
    private static string Assignment(PropertyModel property, string indent)
    {
        string value = Read(property, indent);
        if (property.Direct)
        {
            return $"entity.{Identifier(property.Name)} = {value};";
        }

        string owner = AccessorClass(property, property.TypeArguments);
        string accessor = owner.Length == 0 ? AccessorName(property) : $"{owner}.{AccessorName(property)}";

        return property.Field ? $"{accessor}(entity) = {value};" : $"{accessor}(entity, {value});";
    }

    // One accessor per member, however many classes inherit it. A member of a generic class is set through a
    // generic class of accessors whose type parameters match that class's.
    private static string Accessors(IEnumerable<PropertyModel> accessed)
    {
        StringBuilder accessors = new();
        foreach (IGrouping<string, PropertyModel> owner in accessed
            .Select(static property => property with { TypeArguments = string.Empty })
            .Distinct()
            .GroupBy(static property => AccessorClass(property, property.TypeParameters)))
        {
            bool nested = owner.Key.Length > 0;
            string indent = nested ? "            " : "        ";
            string members = string.Join("\n\n", owner.Select(property => Accessor(property, indent, nested ? "internal" : "private")));

            accessors.AppendLine();
            if (nested)
            {
                accessors.Append("        private static class ").Append(owner.Key).AppendLine(owner.First().Constraints)
                    .AppendLine("        {")
                    .AppendLine(members)
                    .AppendLine("        }");
            }
            else
            {
                accessors.AppendLine(members);
            }
        }

        return accessors.ToString();
    }

    // The [UnsafeAccessor] that sets one member: a field's returns a reference to it, a property's calls its setter.
    private static string Accessor(PropertyModel property, string indent, string access)
    {
        const string UnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
        string type = property.Declared;
        string name = AccessorName(property);

        return property.Field
            ? $"{indent}[{UnsafeAccessor}({UnsafeAccessor}Kind.Field, Name = \"{property.Name}\")]\n"
                + $"{indent}{access} static extern ref {type} {name}({property.Declaring} entity);"
            : $"{indent}[{UnsafeAccessor}({UnsafeAccessor}Kind.Method, Name = \"set_{property.Name}\")]\n"
                + $"{indent}{access} static extern void {name}({property.Declaring} entity, {type} value);";
    }

    // C#'s required members are checked at a new expression. The scene sets a class's required references
    // after construction, so its spawner calls the constructor through an accessor the check does not reach.
    private static string Constructors(IEnumerable<EntityModel> required)
    {
        const string UnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
        StringBuilder constructors = new();
        foreach (EntityModel model in required)
        {
            constructors.AppendLine()
                .Append("        [").Append(UnsafeAccessor).Append('(').Append(UnsafeAccessor).AppendLine("Kind.Constructor)]")
                .Append("        private static extern ").Append(model.QualifiedName).Append(' ').Append(ConstructorName(model))
                .Append('(').Append(model.SpawnModifier).AppendLine("global::Capsule.Scenes.Spawning.EntitySpawn spawn);");
        }

        return constructors.ToString();
    }

    // New and the class's qualified name with every other character an underscore: Game.Door is New_Game_Door.
    private static string ConstructorName(EntityModel model) =>
        "New_" + new string(model.QualifiedName.Substring("global::".Length).Select(static c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    // Set and the member's key capitalized: _tuning is set by SetTuning. A class's keys are unique and never
    // start with a capital, so its accessor names are unique too.
    private static string AccessorName(PropertyModel property) =>
        property.Key.Length == 0 ? "Set" : "Set" + char.ToUpperInvariant(property.Key[0]) + property.Key.Substring(1);

    // The class of accessors for a member of a generic type, named for that type and taking the given type
    // arguments: a member of Game.Held<T> is set through Game_HeldAccessors<T>. Empty for any other member.
    private static string AccessorClass(PropertyModel property, string arguments)
    {
        if (property.TypeParameters.Length == 0)
        {
            return string.Empty;
        }

        string declaring = property.Declaring.Substring("global::".Length);
        string name = new(declaring.Substring(0, declaring.IndexOf('<')).Select(static c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        return $"{name}Accessors<{arguments}>";
    }

    // The read expression for a member whose statement starts at indent. A nullable member tests for a
    // JSON null first. An array reads each element with the same read, off the element's own value.
    private static string Read(PropertyModel property, string indent)
    {
        string key = Literal(property.Key);
        string read = property.Array
            ? $"properties.Array<{property.Type}>({key}, static element => {Element(property, "element", key, indent)})"
            : Element(property, "properties", key, indent);

        return property.Nullable ? $"properties.IsNull({key}) ? null : {read}" : read;
    }

    // One value's read off the properties or array element named by from.
    private static string Element(PropertyModel property, string from, string key, string indent) => property.Kind switch
    {
        PropertyKind.BuiltIn => $"{from}.{PropertySchema.BuiltIn(property.Type)!.Read}({key})",
        PropertyKind.Named => NameSwitch(property, from, key, indent),
        PropertyKind.Flags => FlagSwitch(property, from, key, indent),
        PropertyKind.Asset => Asset(property, from, key),
        PropertyKind.Reference => $"{from}.Entity<{property.Type}>({key})",
        _ => $"{from}.Read<{property.Type}, {property.Converter}>({key})",
    };

    // The flags each name stands for, ORed at load. A switch the compiler checks, so a renamed member fails
    // the build. The casts are unchecked, since a flag of a signed enum may be negative.
    private static string FlagSwitch(PropertyModel property, string from, string key, string indent)
    {
        StringBuilder read = new StringBuilder("unchecked((").Append(property.Type).Append(')').Append(from).Append(".Flags(").Append(key)
            .Append(", static name => name switch\n").Append(indent).Append("{\n");
        foreach ((string json, string field) in property.Names.Items)
        {
            read.Append(indent).Append("    ").Append(Literal(json)).Append(" => (ulong)").Append(property.Type).Append('.').Append(Identifier(field)).Append(",\n");
        }

        string names = string.Join(", ", property.Names.Items.Select(static name => name.Json));

        return read.Append(indent).Append("    _ => (ulong?)null,\n")
            .Append(indent).Append("}, ").Append(Literal(names)).Append("))").ToString();
    }

    private static string Asset(PropertyModel property, string from, string key)
    {
        AssetForm form = PropertySchema.Asset(property.Type)!;

        return $"{from}.{form.Read}({key}, {form.Lookup})";
    }

    // The lookup each asset type an authored member takes resolves its key through, keyed as the build declared each asset.
    private static string Lookups(IEnumerable<PropertyModel> authored, AssetTable assets)
    {
        StringBuilder lookups = new();
        foreach (AssetForm form in PropertySchema.Assets.Where(form => authored.Any(property => property.Kind == PropertyKind.Asset && property.Type == form.Type)))
        {
            lookups.AppendLine()
                .Append("        private static ").Append(form.Type).Append("? ").Append(form.Lookup).AppendLine("(string key) => key switch")
                .AppendLine("        {");
            foreach (KeyValuePair<string, string> asset in assets.Of(form).OrderBy(static asset => asset.Key, StringComparer.Ordinal))
            {
                lookups.Append("            ").Append(Literal(asset.Key)).Append(" => ").Append(asset.Value).AppendLine(",");
            }

            lookups.AppendLine("            _ => null,").AppendLine("        };");
        }

        return lookups.ToString();
    }

    // A switch the compiler checks, so a renamed member or definition fails the build and not the load.
    private static string NameSwitch(PropertyModel property, string from, string key, string indent)
    {
        StringBuilder read = new StringBuilder(from).Append(".Name(").Append(key).Append(") switch\n").Append(indent).Append("{\n");
        foreach ((string json, string field) in property.Names.Items)
        {
            read.Append(indent).Append("    ").Append(Literal(json)).Append(" => ")
                .Append(property.Type).Append('.').Append(Identifier(field)).Append(",\n");
        }

        string names = string.Join(", ", property.Names.Items.Select(static name => name.Json));

        return read.Append(indent).Append("    _ => throw ").Append(from).Append(".NotAName(").Append(key).Append(", ").Append(Literal(names)).Append("),\n")
            .Append(indent).Append('}').ToString();
    }

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private readonly struct Registration(string spawnType, EntityModel model)
    {
        internal string SpawnType { get; } = spawnType;

        internal EntityModel Model { get; } = model;
    }
}
