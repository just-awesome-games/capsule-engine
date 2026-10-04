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

    /// <summary>The declaration of <see cref="SceneDocumentName"/>.</summary>
    internal const string SceneDocument = """
            /// <summary>A shipped scene document, with the baseScene the build read from it. Generated code.</summary>
            [global::System.AttributeUsage(global::System.AttributeTargets.Property)]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            [global::System.Diagnostics.Conditional("CAPSULE_GENERATED_FACTS")]
            internal sealed class CapsuleGeneratedSceneDocumentAttribute : global::System.Attribute
            {
                /// <summary>The document's key.</summary>
                public string? Key { get; set; }

                /// <summary>The key of the abstract scene the document derives from, or null.</summary>
                public string? BaseScene { get; set; }
            }

        """;
}
