using System.ComponentModel;

namespace Capsule.Localization;

/// <summary>A plural category of the Unicode CLDR, which a language's rules select for a count. Generated code returns it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum PluralCategory
{
    /// <summary>The <c>zero</c> form.</summary>
    Zero,

    /// <summary>The <c>one</c> form.</summary>
    One,

    /// <summary>The <c>two</c> form.</summary>
    Two,

    /// <summary>The <c>few</c> form.</summary>
    Few,

    /// <summary>The <c>many</c> form.</summary>
    Many,

    /// <summary>The <c>other</c> form, which every plural text holds.</summary>
    Other,
}
