namespace Capsule.Rendering;

/// <summary>Where a run of text sits on the X axis of the box holding it.</summary>
public enum HorizontalAlignment
{
    /// <summary>Every line starts at the box's left edge. The default.</summary>
    Left,

    /// <summary>Every line is centred in the box, so lines of different widths stay centred on each other.</summary>
    Center,

    /// <summary>Every line ends at the box's right edge.</summary>
    Right,
}
