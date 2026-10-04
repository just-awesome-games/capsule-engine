namespace Capsule.Build.Sheets;

/// <summary>One region of the sheet's texture, with the pivot, sockets and boxes it carries.</summary>
internal readonly record struct SheetFrame(
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    float PivotX,
    float PivotY,
    SheetSocket[] Sockets,
    SheetBox[] Boxes);
