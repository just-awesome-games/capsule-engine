using System.Globalization;
using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

// Checks every game entry of every shipped scene document against the entity class claiming its type, and the
// document's own properties against the scene class composing it. The applier reads the same values again at load,
// and only there does a converter read.
internal static class PlacementCheck
{
    internal static void Run(SourceProductionContext context, PlacementInputs inputs)
    {
        EntityPlan plan = inputs.Entities;
        if (!plan.Generates)
        {
            return;
        }

        Dictionary<string, EntityModel> entities = plan.Registered.Items.ToDictionary(
            static entry => entry.SpawnType, static entry => entry.Model, StringComparer.Ordinal);
        HashSet<string> claimed = new(plan.ClaimedKeys.Items, StringComparer.Ordinal);
        AssetTable assets = new(inputs.Assets.Items, inputs.Documents.Items);
        Dictionary<string, SceneModel?> composing = Composing(inputs.Scenes);

        foreach (SceneDocumentModel document in inputs.Documents.Items.OrderBy(static document => document.Key, StringComparer.Ordinal))
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
                        string keys = claimed.Count == 0 ? "none" : string.Join(", ", plan.ClaimedKeys.Items);
                        Report(context, at, Diagnostics.UnclaimedEntryType, named, entry, placement.Type, keys);
                    }
                }
                else if (entity.CodeOnly)
                {
                    string members = string.Join(", ", entity.Properties.Items
                        .Where(static property => property.CodeOnly)
                        .Select(static property => property.Name));
                    Report(context, at, Diagnostics.CodeOnlyEntryType, named, entry, placement.Type, entity.DisplayName, members);
                }
                else
                {
                    Check(context, at, named, entry, new Owner(entity.DisplayName, entity.Properties), placement.Properties, new Placed(document, entities, assets));
                }
            }

            // A document whose baseScene did not resolve is already its own error.
            if (composing.TryGetValue(document.Key, out SceneModel? scene) && (scene is not null || document.BaseScene is null))
            {
                Owner owner = scene is { } model ? new Owner(model.DisplayName, model.Properties) : new Owner(MetadataNames.Scene, default);
                Location documentAt = Where(document, document.Line == 0 ? 1 : document.Line, document.Column == 0 ? 1 : document.Column);
                Check(context, documentAt, named, "the document", owner, document.Properties, new Placed(document, entities, assets));
            }
        }
    }

    // The class composing each document: the class claiming it, the base a document-only scene's baseScene names,
    // or null for the engine's Scene or a baseScene that did not resolve.
    private static Dictionary<string, SceneModel?> Composing(ScenePlan scenes)
    {
        Dictionary<string, SceneModel?> composing = new(StringComparer.Ordinal);
        foreach (RegisteredScene entry in scenes.Registered.Items)
        {
            if (entry.DocumentName is { } key)
            {
                composing[key] = entry.Model;
            }
        }

        foreach (DocumentOnlyScene document in scenes.DocumentOnly.Items)
        {
            composing[document.DocumentName] = document.Base?.Base;
        }

        return composing;
    }

    private static void Check(
        SourceProductionContext context, Location at, string document, string entry, Owner owner, EquatableArray<(string Name, object? Value)> authored, Placed placed)
    {
        PropertyModel[] settable = owner.Properties.Items.Where(static property => property.Settable).ToArray();
        foreach ((string name, object? value) in authored.Items)
        {
            // An authorable member wins a key it shares with one a document cannot set. A key two
            // authorable members take is already the class's compile error.
            PropertyModel property = owner.Properties.Items
                .Where(declared => declared.Key == name)
                .OrderBy(static declared => declared.Settable ? 0 : 1)
                .FirstOrDefault();
            if (property.Key is null)
            {
                string names = settable.Length == 0 ? "none" : string.Join(", ", settable.Select(static declared => declared.Key));
                Report(context, at, Diagnostics.UnknownEntryProperty, document, entry, name, owner.DisplayName, names);
            }
            else if (property.Refusal is { } refusal)
            {
                Report(context, at, Diagnostics.UnsettableEntryProperty, document, entry, name, $"{owner.DisplayName}.{property.Name}", refusal);
            }
            else if (property.Clash is null)
            {
                CheckValue(new Checked(context, at, document, entry, owner.DisplayName, property, placed), name, value);
            }
        }

        foreach (PropertyModel property in settable)
        {
            if (property.Required && !authored.Items.Any(pair => pair.Name == property.Key))
            {
                Report(context, at, Diagnostics.MissingEntryProperty, document, entry, property.Key, owner.DisplayName);
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
                check.Report(Diagnostics.MismatchedEntryProperty, subject, "null", check.Owner, property.DisplayType, Form(property) + ", or make the member nullable");
            }
        }
        else if (!property.Array)
        {
            CheckElement(check, subject, value);
        }
        else if (value is not EquatableArray<object?> elements)
        {
            check.Report(Diagnostics.MismatchedEntryProperty, subject, Found(value), check.Owner, property.DisplayType, Form(property));
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
            PropertyKind.BuiltIn => PropertyForms.BuiltIn(property.Type)!.Accepts(value),
            PropertyKind.Reference => value is int,
            PropertyKind.Converted => true,
            _ => value is string,
        };

        if (!accepted)
        {
            check.Report(Diagnostics.MismatchedEntryProperty, subject, Found(value), check.Owner, property.ElementDisplay, ElementForm(property));

            return;
        }

        string names = string.Join(", ", property.Names.Items.Select(static known => known.Json));
        switch (property.Kind)
        {
            case PropertyKind.Named when !Names(property, (string)value!):
                check.Report(Diagnostics.UnknownEntryName, subject, Found(value), property.ElementDisplay, names);
                break;

            case PropertyKind.Flags when ((string)value!).Trim().Length > 0:
                foreach (string flag in ((string)value!).Split(',').Select(static flag => flag.Trim()).Where(flag => !Names(property, flag)))
                {
                    check.Report(
                        Diagnostics.UnknownEntryName, subject, $"the name \"{flag}\" in {Found(value)}", property.ElementDisplay,
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
        AssetForm form = PropertyForms.Asset(check.Property.Type)!;
        string? keyed = form.Scene ? AssetPaths.NormalizeKey(authored, out _) : AssetPaths.NormalizePath(authored);
        if (keyed is null || !check.Placed.Assets.Of(form).ContainsKey(keyed))
        {
            check.Report(Diagnostics.UnknownAssetKey, subject, Found(authored), check.Property.ElementDisplay, keyed ?? authored, form.Fix);
        }
    }

    // A reference must name a game entry of the same document whose class the member can hold. An entry whose
    // type no sound class claims is already its own error.
    private static void CheckTarget(Checked check, string subject, int id)
    {
        PlacementModel target = check.Placed.Document.Placements.Items.FirstOrDefault(other => other.Id == id);
        if (target.Type is null)
        {
            check.Report(Diagnostics.UnknownEntityReference, subject, id);
        }
        else if (check.Placed.Entities.TryGetValue(target.Type, out EntityModel targeted) && !targeted.AssignableTo.Items.Contains(check.Property.Type))
        {
            check.Report(
                Diagnostics.MismatchedEntityReference, subject, id, targeted.DisplayName,
                $"{check.Owner}.{check.Property.Name}", check.Property.ElementDisplay);
        }
    }

    private static string Form(PropertyModel property) =>
        property.Array ? "an array, [a, b, c], of " + ElementForm(property) : ElementForm(property);

    private static string ElementForm(PropertyModel property) => property.Kind switch
    {
        PropertyKind.BuiltIn => PropertyForms.BuiltIn(property.Type)!.Form,
        PropertyKind.Named => "a name in quotes",
        PropertyKind.Flags => "member names in quotes, joined by commas",
        PropertyKind.Asset => PropertyForms.Asset(property.Type)!.Form,
        PropertyKind.Reference => PropertyForms.ReferenceForm,
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
    private static Location Where(SceneDocumentModel document, PlacementModel placement) => Where(document, placement.Line, placement.Column);

    private static Location Where(SceneDocumentModel document, int line, int column)
    {
        if (document.Path is not { } path || line == 0)
        {
            return Location.None;
        }

        LinePosition start = new(line - 1, column - 1);

        return Location.Create(path, default, new LinePositionSpan(start, start));
    }

    private static void Report(SourceProductionContext context, Location at, DiagnosticDescriptor descriptor, params object[] arguments) =>
        context.ReportDiagnostic(Diagnostic.Create(descriptor, at, arguments));

    // The document being checked, every registered entity by key, which a reference's target is checked against,
    // and every asset the build declared, which an asset key is checked against.
    private readonly record struct Placed(SceneDocumentModel Document, Dictionary<string, EntityModel> Entities, AssetTable Assets);

    // The class whose members an entry's or a document's properties set, as a report names it.
    private readonly record struct Owner(string DisplayName, EquatableArray<PropertyModel> Properties);

    // One authored member of one entry or document being checked, and where its failures report.
    private readonly record struct Checked(
        SourceProductionContext Context, Location At, string Document, string Entry, string Owner, PropertyModel Property, Placed Placed)
    {
        // Each report names the document and the entry first.
        internal void Report(DiagnosticDescriptor descriptor, params object[] arguments) =>
            PlacementCheck.Report(Context, At, descriptor, [Document, Entry, .. arguments]);
    }
}
