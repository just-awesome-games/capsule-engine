using System.Diagnostics;
using Capsule.Diagnostics;

namespace Capsule.Scenes;

/// <summary>The <see cref="DebugPanel"/> rows that name an entity.</summary>
public static class DebugPanelExtensions
{
    /// <summary>Writes <paramref name="value"/>'s name under <paramref name="label"/> as a row that opens its panel.</summary>
    /// <remarks>Null shows as <c>null</c>.</remarks>
    [Conditional(Development.Symbol)]
    public static void Field(this DebugPanel panel, string label, Entity? value)
    {
        ArgumentNullException.ThrowIfNull(panel);

        panel.Link(label, value?.Name, value);
    }
}
