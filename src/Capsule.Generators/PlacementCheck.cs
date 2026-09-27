using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

/// <summary>One game entry of a shipped document, as its <c>CapsuleGeneratedPlacement</c> attribute carries it.</summary>
/// <param name="Properties">
/// Each authored property with its value: a bool, an int, a double, a string, null, an
/// <see cref="EquatableArray{T}"/> of values, or <see cref="JsonObject"/>.
/// </param>
/// <param name="Line">The line the entry starts on in the document's file, from 1, or 0 when unknown.</param>
/// <param name="Column">The column the entry starts at, from 1, or 0 when unknown.</param>
internal readonly record struct PlacementModel(
    int Id,
    string Type,
    EquatableArray<(string Name, object? Value)> Properties,
    int Line,
    int Column)
{
    /// <summary>What a JSON object arrives as. The build writes one <c>typeof(object)</c>.</summary>
    internal static readonly object JsonObject = new();

    internal static PlacementModel? From(AttributeData attribute)
    {
        ImmutableArray<TypedConstant> arguments = attribute.ConstructorArguments;
        if (arguments.Length != 3 || arguments[0].Value is not int id || arguments[1].Value is not string type || arguments[2].Kind != TypedConstantKind.Array)
        {
            return null;
        }

        ImmutableArray<TypedConstant> pairs = arguments[2].Values;
        ImmutableArray<(string, object?)>.Builder properties = ImmutableArray.CreateBuilder<(string, object?)>();
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            properties.Add(((string)pairs[i].Value!, Value(pairs[i + 1])));
        }

        int line = 0;
        int column = 0;
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Line":
                    line = named.Value.Value as int? ?? 0;
                    break;
                case "Column":
                    column = named.Value.Value as int? ?? 0;
                    break;
            }
        }

        return new PlacementModel(id, type, new(properties.ToImmutable()), line, column);
    }

    private static object? Value(TypedConstant constant) => constant.Kind switch
    {
        TypedConstantKind.Array => new EquatableArray<object?>(constant.Values.Select(Value).ToImmutableArray()),
        TypedConstantKind.Type => JsonObject,
        _ => constant.Value,
    };
}

// Checks every game entry of every shipped scene document against the class claiming its type. The applier
// reads the same values again at load, and only there does a converter read.
internal static class PlacementCheck
{
    /// <param name="entities">Every registered entity, by key.</param>
    /// <param name="claimed">Every key any entity class claims, faulted or not.</param>
    internal static void Run(
        SourceProductionContext context,
        ImmutableArray<SceneDocumentInfo> documents,
        Dictionary<string, EntityModel> entities,
        HashSet<string> claimed)
    {
        foreach (SceneDocumentInfo document in documents.OrderBy(static document => document.Key, StringComparer.Ordinal))
        {
            string named = document.Source is { } source
                ? $"'{document.Key}' (derived from {source})"
                : $"'{document.Key}'";

            foreach (PlacementModel placement in document.Placements.Items)
            {
                string entry = "entity " + placement.Id.ToString(CultureInfo.InvariantCulture);
                Location at = Where(document, placement);
                if (!entities.TryGetValue(placement.Type, out EntityModel entity))
                {
                    if (!claimed.Contains(placement.Type))
                    {
                        string keys = claimed.Count == 0 ? "none" : string.Join(", ", claimed.OrderBy(static key => key, StringComparer.Ordinal));
                        Report(context, at, RegistryDiagnostics.UnclaimedEntryType, named, entry, placement.Type, keys);
                    }
                }
                else if (entity.CodeOnly)
                {
                    string members = string.Join(", ", entity.Properties.Items
                        .Where(static property => property.RequiredKeyword)
                        .Select(static property => property.Name));
                    Report(context, at, RegistryDiagnostics.CodeOnlyEntryType, named, entry, placement.Type, entity.DisplayName, members);
                }
                else
                {
                    Check(context, at, named, entry, entity, placement);
                }
            }
        }
    }

    private static void Check(SourceProductionContext context, Location at, string document, string entry, EntityModel entity, PlacementModel placement)
    {
        PropertyModel[] settable = entity.Properties.Items.Where(static property => property.Settable).ToArray();
        foreach ((string name, object? value) in placement.Properties.Items)
        {
            // An authorable member wins a key it shares with one a placement cannot set. A key two
            // authorable members take is already the class's compile error.
            PropertyModel property = entity.Properties.Items
                .Where(declared => declared.Key == name)
                .OrderBy(static declared => declared.Settable ? 0 : 1)
                .FirstOrDefault();
            if (property.Key is null)
            {
                string names = settable.Length == 0 ? "none" : string.Join(", ", settable.Select(static declared => declared.Key));
                Report(context, at, RegistryDiagnostics.UnknownEntryProperty, document, entry, name, entity.DisplayName, names);
            }
            else if (property.Refusal is { } refusal)
            {
                Report(context, at, RegistryDiagnostics.UnsettableEntryProperty, document, entry, name, $"{entity.DisplayName}.{property.Name}", refusal);
            }
            else if (property.Clash is not null)
            {
                continue;
            }
            else if (!Accepts(property, value))
            {
                Report(
                    context, at, RegistryDiagnostics.MismatchedEntryProperty, document, entry, name, Found(value), entity.DisplayName, property.DisplayType,
                    value is null ? Form(property) + ", or make the member nullable" : Form(property));
            }
            else if (property.Kind == PropertyKind.Named && value is string named && !property.Names.Items.Any(known => known.Json == named))
            {
                string names = string.Join(", ", property.Names.Items.Select(static known => known.Json));
                Report(context, at, RegistryDiagnostics.UnknownEntryName, document, entry, name, Found(value), property.DisplayType.TrimEnd('?'), names);
            }
        }

        foreach (PropertyModel property in settable)
        {
            if (property.Required && !placement.Properties.Items.Any(authored => authored.Name == property.Key))
            {
                Report(context, at, RegistryDiagnostics.MissingEntryProperty, document, entry, property.Key, entity.DisplayName);
            }
        }
    }

    // A converter is the only judge of its type's JSON, and it reads at load. Every member refuses null
    // unless it is nullable.
    private static bool Accepts(PropertyModel property, object? value) => value is null
        ? property.Nullable
        : property.Kind switch
        {
            PropertyKind.BuiltIn => PropertySchema.BuiltIn(property.Type)!.Accepts(value),
            PropertyKind.Named => value is string,
            _ => true,
        };

    private static string Form(PropertyModel property) => property.Kind switch
    {
        PropertyKind.BuiltIn => PropertySchema.BuiltIn(property.Type)!.Form,
        PropertyKind.Named => "a name in quotes",
        _ => "the form its converter reads",
    };

    // What the entry wrote, in the words the applier's own message uses at load.
    private static string Found(object? value) => value switch
    {
        null => "null",
        bool flag => flag ? "true" : "false",
        int or double => "the number " + Convert.ToString(value, CultureInfo.InvariantCulture),
        string text => "the string \"" + text + "\"",
        EquatableArray<object?> => "an array",
        _ => "an object",
    };

    // The entry's opening brace in the document's file, which an editor opens at.
    private static Location Where(SceneDocumentInfo document, PlacementModel placement)
    {
        if (document.Path is not { } path || placement.Line == 0)
        {
            return Location.None;
        }

        LinePosition start = new(placement.Line - 1, placement.Column - 1);

        return Location.Create(path, default, new LinePositionSpan(start, start));
    }

    private static void Report(SourceProductionContext context, Location at, DiagnosticDescriptor descriptor, params object[] arguments) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, at, arguments));
}
