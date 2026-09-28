namespace Capsule.Build.Configuration;

/// <summary>A <c>&lt;name&gt;.atlas.json</c>, which declares the atlas <c>&lt;name&gt;</c> and holds its own settings.</summary>
internal sealed class AtlasConfigJson
{
    /// <summary>The page extent when the file sets none.</summary>
    internal const int DefaultMaxSize = 4096;

    /// <summary>The largest page extent a file may set.</summary>
    internal const int LargestMaxSize = 8192;

    /// <summary>What an atlas file holds, as a refusal describes it.</summary>
    internal const string Shape = "An atlas file holds only the atlas's own settings, as {} or { \"maxSize\": 2048 }. "
        + "\"maxSize\" is the largest page extent, a power of two up to 8192, and 4096 by default.";

    /// <summary>The largest extent a page reaches on either axis, or null for <see cref="DefaultMaxSize"/>.</summary>
    public int? MaxSize { get; set; }
}
