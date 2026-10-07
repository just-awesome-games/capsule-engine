using System.Text;

namespace Capsule.Generators;

// The statements an applier sets authored members with, and the expression each reads its value with off the
// members of an entry, of the document or of an object a member holds.
internal static class PropertyReadRenderer
{
    // Sets each member on target, typed targetType: a required one always, and an optional one only where the
    // members author its key. An entity reference is set once every entry is built. Each statement starts at
    // indent and ends with a newline.
    internal static string Assignments(IEnumerable<PropertyModel> properties, string target, string targetType, string indent)
    {
        StringBuilder statements = new();
        string nested = indent + "    ";
        foreach (PropertyModel property in properties)
        {
            if (property.Required)
            {
                statements.Append(indent).Append(Statement(property, target, targetType, indent)).Append('\n');
                continue;
            }

            statements.Append(indent).Append("if (members.Has(").Append(CodeText.Literal(property.Key)).Append("))\n")
                .Append(indent).Append("{\n")
                .Append(nested).Append(Statement(property, target, targetType, nested)).Append('\n')
                .Append(indent).Append("}\n");
        }

        return statements.ToString();
    }

    // An object the member holds without a setter is filled in place. A reference waits for every entry.
    private static string Statement(PropertyModel property, string target, string targetType, string indent)
    {
        if (property.Held)
        {
            return $"{ObjectRenderer.Builder(property.Type)}({Current(property, target)}, members.Object({CodeText.Literal(property.Key)}), replaces: false);";
        }

        return property.Kind == PropertyKind.Reference
            ? $"members.Link({target}, static (owner, linked) => {Assignment(property, $"(({targetType})owner)", "linked", indent).TrimEnd(';')});"
            : Assignment(property, target, "members", indent);
    }

    // The read expression for a member whose statement starts at indent. A nullable member tests for a
    // JSON null first. An array reads each element with the same read, off the element's own value. A memory
    // shares the elements one read of the document produced.
    private static string Read(PropertyModel property, string target, string from, string indent)
    {
        string key = CodeText.Literal(property.Key);
        string read = property.Array
            ? $"{from}.{(property.Shared ? "Shared" : "Array")}<{property.Type}>({key}, static element => {Element(property, "element", key, "null", indent)})"
            : Element(property, from, key, property.Kind == PropertyKind.Object ? Current(property, target) : string.Empty, indent);

        return property.Nullable ? $"{from}.IsNull({key}) ? null : {read}" : read;
    }

    // Plain C# where the game can assign the member, and an accessor where it cannot.
    private static string Assignment(PropertyModel property, string target, string from, string indent)
    {
        string value = Read(property, target, from, indent);
        if (property.Direct)
        {
            return $"{target}.{CodeText.Identifier(property.Name)} = {value};";
        }

        string accessor = EntityAccessorRenderer.SetterReference(property);

        return property.Field ? $"{accessor}({target}) = {value};" : $"{accessor}({target}, {value});";
    }

    // The member's current value on target, which an object read fills when it names no type.
    private static string Current(PropertyModel property, string target) =>
        property.Readable ? $"{target}.{CodeText.Identifier(property.Name)}"
        : property.Field ? $"{EntityAccessorRenderer.SetterReference(property)}({target})"
        : $"{EntityAccessorRenderer.GetterReference(property)}({target})";

    // One value's read off the members or array element named by from. An object read is handed the
    // instance it may fill.
    private static string Element(PropertyModel property, string from, string key, string held, string indent) => property.Kind switch
    {
        PropertyKind.BuiltIn => $"{from}.{PropertyForms.BuiltIn(property.Type)!.Read}({key})",
        PropertyKind.Named => NameSwitch(property, from, key, indent),
        PropertyKind.Flags => FlagSwitch(property, from, key, indent),
        PropertyKind.Asset => AssetRead(property, from, key),
        PropertyKind.Reference => $"{from}.Entity<{property.Type}>({key})",
        PropertyKind.Object => $"{ObjectRenderer.Builder(property.Type)}({held}, {from}.Object({key}), replaces: true)",
        _ => $"{from}.Read<{property.Type}, {property.Converter}>({key})",
    };

    // A switch the compiler checks, so a renamed member or definition fails the build and not the load.
    private static string NameSwitch(PropertyModel property, string from, string key, string indent)
    {
        StringBuilder read = new StringBuilder(from).Append(".Name(").Append(key).Append(") switch\n").Append(indent).Append("{\n");
        foreach ((string json, string field) in property.Names.Items)
        {
            read.Append(indent).Append("    ").Append(CodeText.Literal(json)).Append(" => ")
                .Append(property.Type).Append('.').Append(CodeText.Identifier(field)).Append(",\n");
        }

        return read.Append(indent).Append("    _ => throw ").Append(from).Append(".NotAName(").Append(key).Append(", ").Append(CodeText.Literal(NameList(property))).Append("),\n")
            .Append(indent).Append('}').ToString();
    }

    // The flags each name stands for, ORed at load. A switch the compiler checks, so a renamed member fails
    // the build. The casts are unchecked, since a flag of a signed enum may be negative.
    private static string FlagSwitch(PropertyModel property, string from, string key, string indent)
    {
        StringBuilder read = new StringBuilder("unchecked((").Append(property.Type).Append(')').Append(from).Append(".Flags(").Append(key)
            .Append(", static name => name switch\n").Append(indent).Append("{\n");
        foreach ((string json, string field) in property.Names.Items)
        {
            read.Append(indent).Append("    ").Append(CodeText.Literal(json)).Append(" => (ulong)").Append(property.Type).Append('.').Append(CodeText.Identifier(field)).Append(",\n");
        }

        return read.Append(indent).Append("    _ => (ulong?)null,\n")
            .Append(indent).Append("}, ").Append(CodeText.Literal(NameList(property))).Append("))").ToString();
    }

    private static string AssetRead(PropertyModel property, string from, string key)
    {
        AssetForm form = PropertyForms.Asset(property.Type)!;

        return $"{from}.{form.Read}({key}, {form.Lookup})";
    }

    // Every JSON name the member takes, as a load failure lists them.
    private static string NameList(PropertyModel property) => string.Join(", ", property.Names.Items.Select(static name => name.Json));
}
