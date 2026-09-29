namespace Capsule.Build.Registry;

/// <summary>
/// The attributes generated members carry for the generator, each declared beside <c>CapsuleAssets</c>
/// by the step whose members use it. Each is conditional on a symbol no build defines. The generator
/// reads it from source, and the compiler leaves it out of the assembly.
/// </summary>
internal static class GeneratedAttributes
{
    /// <summary>What marks a texture or sound member with the key and extension a scene document names it by.</summary>
    internal const string AssetName = "CapsuleGeneratedAsset";

    /// <summary>What marks a constant as a shipped scene document.</summary>
    internal const string SceneDocumentName = "CapsuleGeneratedSceneDocument";

    /// <summary>What marks each game entry of a shipped scene document.</summary>
    internal const string PlacementName = "CapsuleGeneratedPlacement";

    /// <summary>The declaration of <see cref="AssetName"/>.</summary>
    internal const string Asset = """
            /// <summary>A texture or sound, with the key and extension a scene document names it by. Generated code.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            [global::System.Diagnostics.Conditional("CAPSULE_GENERATED_FACTS")]
            internal sealed class CapsuleGeneratedAssetAttribute : global::System.Attribute
            {
                /// <summary>The asset's key and extension.</summary>
                public CapsuleGeneratedAssetAttribute(string path)
                {
                }
            }

        """;

    /// <summary>The declarations of <see cref="SceneDocumentName"/> and <see cref="PlacementName"/>.</summary>
    internal const string SceneDocument = """
            /// <summary>A shipped scene document, with the settings the build read from it. Generated code.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            [global::System.Diagnostics.Conditional("CAPSULE_GENERATED_FACTS")]
            internal sealed class CapsuleGeneratedSceneDocumentAttribute : global::System.Attribute
            {
                /// <summary>The document's key.</summary>
                public string? Key { get; set; }

                /// <summary>The key of the abstract scene the document derives from, or null.</summary>
                public string? BaseScene { get; set; }

                /// <summary>The key of the camera the document installs, or null.</summary>
                public string? Camera { get; set; }

                /// <summary>The file an authoring module derived the document from, or null for a hand-authored one.</summary>
                public string? Source { get; set; }

                /// <summary>The file the build read, relative to the project, where the compiler reports an entry's error.</summary>
                public string? Path { get; set; }

                /// <summary>Each of the document's own properties' name and value in turn, as a placement carries its own, or null.</summary>
                public object?[]? Properties { get; set; }

                /// <summary>The line the document's properties start on, counted from 1.</summary>
                public int Line { get; set; }

                /// <summary>The column the document's properties start at on that line, counted from 1.</summary>
                public int Column { get; set; }
            }

            /// <summary>One game entry of a shipped scene document, which the compiler checks against the class claiming its type. Generated code.</summary>
            /// <remarks>
            /// Each property is its name, then its JSON value as a C# constant: a bool, an int, a double, a string,
            /// null or an object array. A JSON object is written typeof(object), since only a converter reads one.
            /// </remarks>
            [global::System.AttributeUsage(global::System.AttributeTargets.Property, AllowMultiple = true)]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            [global::System.Diagnostics.Conditional("CAPSULE_GENERATED_FACTS")]
            internal sealed class CapsuleGeneratedPlacementAttribute : global::System.Attribute
            {
                /// <summary>The entry's id, type, and each property's name and value in turn.</summary>
                public CapsuleGeneratedPlacementAttribute(int id, string type, params object?[] properties)
                {
                }

                /// <summary>The line the entry starts on in the file the build read, counted from 1.</summary>
                public int Line { get; set; }

                /// <summary>The column the entry starts at on that line, counted from 1.</summary>
                public int Column { get; set; }
            }

        """;
}
