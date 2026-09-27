using System.Collections.Immutable;
using System.Globalization;
using Capsule.Assets;
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
    /// <param name="assets">Every texture, sound and scene document the build declared.</param>
    internal static void Run(
        SourceProductionContext context,
        ImmutableArray<SceneDocumentInfo> documents,
        Dictionary<string, EntityModel> entities,
        HashSet<string> claimed,
        AssetTable assets)
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
                        .Where(static property => property.CodeOnly)
                        .Select(static property => property.Name));
                    Report(context, at, RegistryDiagnostics.CodeOnlyEntryType, named, entry, placement.Type, entity.DisplayName, members);
                }
                else
                {
                    Check(context, at, named, entry, entity, placement, new Placed(document, entities, assets));
                }
            }
        }
    }

    private static void Check(SourceProductionContext context, Location at, string document, string entry, EntityModel entity, PlacementModel placement, Placed placed)
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
            else if (property.Clash is null)
            {
                CheckValue(new Checked(context, at, document, entry, entity, property, placed), name, value);
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

    // Every member refuses null unless it is nullable. An array's elements are each checked as a single value,
    // and a report names the element by its index.
    private static void CheckValue(Checked check, string name, object? value)
    {
        PropertyModel property = check.Property;
        string subject = $"'{name}'";
        if (value is null)
        {
            if (!property.Nullable)
            {
                check.Report(RegistryDiagnostics.MismatchedEntryProperty, subject, "null", check.Entity.DisplayName, property.DisplayType, Form(property) + ", or make the member nullable");
            }
        }
        else if (!property.Array)
        {
            CheckElement(check, subject, value);
        }
        else if (value is not EquatableArray<object?> elements)
        {
            check.Report(RegistryDiagnostics.MismatchedEntryProperty, subject, Found(value), check.Entity.DisplayName, property.DisplayType, Form(property));
        }
        else
        {
            for (int i = 0; i < elements.Items.Length; i++)
            {
                CheckElement(check, string.Format(CultureInfo.InvariantCulture, "'{0}' element {1}", name, i), elements.Items[i]);
            }
        }
    }

    // A converter is the only judge of its type's JSON, and it reads at load.
    private static void CheckElement(Checked check, string subject, object? value)
    {
        PropertyModel property = check.Property;
        bool accepted = value is not null && property.Kind switch
        {
            PropertyKind.BuiltIn => PropertySchema.BuiltIn(property.Type)!.Accepts(value),
            PropertyKind.Reference => value is int,
            PropertyKind.Converted => true,
            _ => value is string,
        };

        if (!accepted)
        {
            check.Report(RegistryDiagnostics.MismatchedEntryProperty, subject, Found(value), check.Entity.DisplayName, property.ElementDisplay, ElementForm(property));

            return;
        }

        string names = string.Join(", ", property.Names.Items.Select(static known => known.Json));
        switch (property.Kind)
        {
            case PropertyKind.Named when !Names(property, (string)value!):
                check.Report(RegistryDiagnostics.UnknownEntryName, subject, Found(value), property.ElementDisplay, names);
                break;

            case PropertyKind.Flags when ((string)value!).Trim().Length > 0:
                foreach (string flag in ((string)value!).Split(',').Select(static flag => flag.Trim()).Where(flag => !Names(property, flag)))
                {
                    check.Report(
                        RegistryDiagnostics.UnknownEntryName, subject, $"the name \"{flag}\" in {Found(value)}", property.ElementDisplay,
                        names + ", joined by commas");
                }

                break;

            case PropertyKind.Asset:
                CheckAsset(check, subject, (string)value!);
                break;

            case PropertyKind.Reference:
                CheckTarget(check, subject, (int)value!);
                break;
        }
    }

    private static bool Names(PropertyModel property, string name) => property.Names.Items.Any(known => known.Json == name);

    // A key is checked as the load resolves it: normalized, then looked up among the assets the build declared.
    private static void CheckAsset(Checked check, string subject, string authored)
    {
        AssetForm form = PropertySchema.Asset(check.Property.Type)!;
        string? keyed = form.Scene ? AssetPaths.NormalizeKey(authored, out _) : AssetPaths.NormalizePath(authored);
        if (keyed is null || !check.Placed.Assets.Of(form).ContainsKey(keyed))
        {
            check.Report(RegistryDiagnostics.UnknownAssetKey, subject, Found(authored), check.Property.ElementDisplay, keyed ?? authored, form.Fix);
        }
    }

    // A reference must name a game entry of the same document whose class the member can hold. An entry whose
    // type no sound class claims is already its own error.
    private static void CheckTarget(Checked check, string subject, int id)
    {
        PlacementModel target = check.Placed.Document.Placements.Items.FirstOrDefault(other => other.Id == id);
        if (target.Type is null)
        {
            check.Report(RegistryDiagnostics.UnknownEntityReference, subject, id);
        }
        else if (check.Placed.Entities.TryGetValue(target.Type, out EntityModel targeted) && !targeted.AssignableTo.Items.Contains(check.Property.Type))
        {
            check.Report(
                RegistryDiagnostics.MismatchedEntityReference, subject, id, targeted.DisplayName,
                $"{check.Entity.DisplayName}.{check.Property.Name}", check.Property.ElementDisplay);
        }
    }

    private static string Form(PropertyModel property) =>
        property.Array ? "an array, [a, b, c], of " + ElementForm(property) : ElementForm(property);

    private static string ElementForm(PropertyModel property) => property.Kind switch
    {
        PropertyKind.BuiltIn => PropertySchema.BuiltIn(property.Type)!.Form,
        PropertyKind.Named => "a name in quotes",
        PropertyKind.Flags => "member names in quotes, joined by commas",
        PropertyKind.Asset => PropertySchema.Asset(property.Type)!.Form,
        PropertyKind.Reference => PropertySchema.ReferenceForm,
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

    // The document being checked, every registered entity by key, which a reference's target is checked against,
    // and every asset the build declared, which an asset key is checked against.
    private readonly record struct Placed(SceneDocumentInfo Document, Dictionary<string, EntityModel> Entities, AssetTable Assets);

    // One authored member of one entry being checked, and where its failures report.
    private readonly record struct Checked(
        SourceProductionContext Context, Location At, string Document, string Entry, EntityModel Entity, PropertyModel Property, Placed Placed)
    {
        // Each report names the document and the entry first.
        internal void Report(DiagnosticDescriptor descriptor, params object[] arguments) =>
            PlacementCheck.Report(Context, At, descriptor, [Document, Entry, .. arguments]);
    }
}
