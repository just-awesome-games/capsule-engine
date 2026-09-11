namespace Capsule.Rendering;

/// <summary>Where a run of text sits on the Y axis of the box holding it.</summary>
public enum VerticalAlignment
{
    /// <summary>The run's first line starts at the box's top edge. The default.</summary>
    Top,

    /// <summary>The run is centred between the box's top and bottom edges.</summary>
    Middle,

    /// <summary>The run's last line ends at the box's bottom edge.</summary>
    Bottom,
}
