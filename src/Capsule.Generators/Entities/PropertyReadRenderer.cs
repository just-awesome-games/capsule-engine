using System.Text;

namespace Capsule.Generators;

// The expression an applier reads one authored member's value with, off the entry's properties.
internal static class PropertyReadRenderer
{
    // The read expression for a member whose statement starts at indent. A nullable member tests for a
    // JSON null first. An array reads each element with the same read, off the element's own value.
    internal static string Read(PropertyModel property, string indent)
    {
        string key = CodeText.Literal(property.Key);
        string read = property.Array
            ? $"properties.Array<{property.Type}>({key}, static element => {Element(property, "element", key, indent)})"
            : Element(property, "properties", key, indent);

        return property.Nullable ? $"properties.IsNull({key}) ? null : {read}" : read;
    }

    // One value's read off the properties or array element named by from.
    private static string Element(PropertyModel property, string from, string key, string indent) => property.Kind switch
    {
        PropertyKind.BuiltIn => $"{from}.{PropertyForms.BuiltIn(property.Type)!.Read}({key})",
        PropertyKind.Named => NameSwitch(property, from, key, indent),
        PropertyKind.Flags => FlagSwitch(property, from, key, indent),
        PropertyKind.Asset => AssetRead(property, from, key),
        PropertyKind.Reference => $"{from}.Entity<{property.Type}>({key})",
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
