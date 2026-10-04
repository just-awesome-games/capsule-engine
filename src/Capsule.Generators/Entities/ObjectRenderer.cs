using System.Text;
using Microsoft.CodeAnalysis;

namespace Capsule.Generators;

/// <summary>One object class with the subclasses a type key constructs, each by the key it claims.</summary>
internal readonly record struct KeyedObject(ObjectModel Model, EquatableArray<(string Key, string Type)> Types);

// The methods an applier reads a member's JSON object through: one Fill per class, setting its members, and one
// Object per member type, which picks the instance to fill. CapsuleEntities and CapsuleScenes each declare those
// their own members reach.
internal static class ObjectRenderer
{
    private const string Members = "global::Capsule.Scenes.Spawning.AuthoredMembers";

    // A Fill method's statements sit one level inside its braces.
    private const string StatementIndent = "            ";

    /// <summary>
    /// Every class the models reach, once, with the keys of the subclasses each can construct. Two subclasses of one
    /// class claiming one key are refused, naming both, and the first by declaration keeps it.
    /// </summary>
    internal static EquatableArray<KeyedObject> Keyed(IEnumerable<ObjectModel> reached, string rootNamespace, List<Diagnostic> diagnostics)
    {
        Dictionary<string, ObjectModel> objects = new(StringComparer.Ordinal);
        foreach (ObjectModel model in reached)
        {
            objects[model.QualifiedName] = model;
        }

        List<KeyedObject> keyed = [];
        foreach (ObjectModel model in objects.Values.OrderBy(static model => model.QualifiedName, StringComparer.Ordinal))
        {
            Dictionary<string, ObjectModel> claimed = RegistryPass.Keyed(
                diagnostics,
                model.Subclasses.Items.Select(name => objects[name]).Where(static subclass => subclass.Construction != ObjectConstruction.None),
                rootNamespace);

            keyed.Add(new KeyedObject(model, new([.. claimed.OrderBy(static entry => entry.Key, StringComparer.Ordinal).Select(static entry => (entry.Key, entry.Value.QualifiedName))])));
        }

        return new([.. keyed]);
    }

    /// <summary>Every authored member the objects declare, which the generated class sets through its accessors and lookups.</summary>
    internal static IEnumerable<PropertyModel> Authored(EquatableArray<KeyedObject> objects) =>
        objects.Items.SelectMany(static entry => entry.Model.Authored);

    // Game.Movement's builder is Object_Game_Movement.
    internal static string Builder(string qualifiedName) => "Object_" + CodeText.TypeIdentifier(qualifiedName);

    /// <summary>
    /// The builder of each member type the members reach, the Fill of every class, and the constructor accessor of
    /// each class only an accessor constructs.
    /// </summary>
    /// <param name="members">The members the generated class reads directly, whose object types need a builder.</param>
    internal static string Methods(EquatableArray<KeyedObject> objects, IEnumerable<PropertyModel> members)
    {
        Dictionary<string, KeyedObject> byName = objects.Items.ToDictionary(static entry => entry.Model.QualifiedName, StringComparer.Ordinal);
        IEnumerable<string> built = members.Concat(Authored(objects))
            .Where(static property => property.Kind == PropertyKind.Object && property.Settable)
            .Select(static property => property.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static type => type, StringComparer.Ordinal);

        StringBuilder methods = new();
        foreach (string type in built)
        {
            methods.Append(Build(byName[type], byName));
        }

        foreach (KeyedObject entry in objects.Items)
        {
            methods.Append(Fill(entry.Model));
        }

        foreach (KeyedObject entry in objects.Items.Where(static entry => entry.Model.Construction == ObjectConstruction.Accessor))
        {
            methods.Append(EntityAccessorRenderer.Constructor(entry.Model.QualifiedName, string.Empty));
        }

        return methods.ToString();
    }

    // Picks the instance a member's object fills: the subclass its type key names, or with no type the instance the
    // member holds, as its own class, or a new one of the member's class. A member with no setter replaces nothing,
    // so its type key stays unread and the document refuses it.
    private static string Build(KeyedObject entry, Dictionary<string, KeyedObject> byName)
    {
        ObjectModel model = entry.Model;
        string name = model.QualifiedName;
        string types = CodeText.Literal(string.Join(", ", entry.Types.Items.Select(static type => type.Key)));
        string fresh = Construct(model) ?? $"throw members.NotAType({types})";
        StringBuilder arms = new();
        if (model.Subclasses.Items.IsEmpty)
        {
            arms.Append($"            null => {Fill(name)}(held ?? {fresh}, members),\n");
        }
        else
        {
            arms.Append("            null => held switch\n            {\n");
            foreach (string subclass in model.Subclasses.Items)
            {
                arms.Append($"                {subclass} subclass => {Fill(subclass)}(subclass, members),\n");
            }

            // A throw expression cannot be an argument, so a class nothing constructs throws as the arm itself.
            string unheld = Construct(model) is { } constructed ? $"{Fill(name)}({constructed}, members)" : fresh;
            arms.Append($"                null => {unheld},\n")
                .Append($"                _ => {Fill(name)}(held, members),\n")
                .Append("            },\n");
        }

        foreach ((string key, string type) in entry.Types.Items)
        {
            arms.Append($"            {CodeText.Literal(key)} => {Fill(type)}({Construct(byName[type].Model)}, members),\n");
        }

        return $$"""

                    private static {{name}} {{Builder(name)}}({{name}}? held, {{Members}} members, bool replaces) => (replaces ? members.Type() : null) switch
                    {
            {{arms}}            _ => throw members.NotAType({{types}}),
                    };

            """;
    }

    private static string Fill(ObjectModel model) => $$"""

                private static {{model.QualifiedName}} {{Fill(model.QualifiedName)}}({{model.QualifiedName}} target, {{Members}} members)
                {
        {{PropertyReadRenderer.Assignments(model.Authored, "target", model.QualifiedName, StatementIndent)}}            return target;
                }

        """;

    private static string Fill(string qualifiedName) => "Fill_" + CodeText.TypeIdentifier(qualifiedName);

    private static string? Construct(ObjectModel model) => model.Construction switch
    {
        ObjectConstruction.New => $"new {model.QualifiedName}()",
        ObjectConstruction.Accessor => $"{EntityAccessorRenderer.ConstructorName(model.QualifiedName)}()",
        _ => null,
    };
}
