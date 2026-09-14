namespace Capsule.Rendering;

/// <summary>
/// The CSS named colours, alphabetical, each opaque (alpha 255). <see cref="Green"/> is the CSS
/// <c>(0, 128, 0)</c>; the bright green is <see cref="Lime"/>.
/// </summary>
public readonly partial record struct ColorRgba
{
    /// <summary>CSS <c>aliceblue</c>: (240, 248, 255).</summary>
    public static ColorRgba AliceBlue => new(240, 248, 255);

    /// <summary>CSS <c>antiquewhite</c>: (250, 235, 215).</summary>
    public static ColorRgba AntiqueWhite => new(250, 235, 215);

    /// <summary>CSS <c>aqua</c>: (0, 255, 255).</summary>
    public static ColorRgba Aqua => new(0, 255, 255);

    /// <summary>CSS <c>aquamarine</c>: (127, 255, 212).</summary>
    public static ColorRgba Aquamarine => new(127, 255, 212);

    /// <summary>CSS <c>azure</c>: (240, 255, 255).</summary>
    public static ColorRgba Azure => new(240, 255, 255);

    /// <summary>CSS <c>beige</c>: (245, 245, 220).</summary>
    public static ColorRgba Beige => new(245, 245, 220);

    /// <summary>CSS <c>bisque</c>: (255, 228, 196).</summary>
    public static ColorRgba Bisque => new(255, 228, 196);

    /// <summary>CSS <c>black</c>: (0, 0, 0).</summary>
    public static ColorRgba Black => new(0, 0, 0);

    /// <summary>CSS <c>blanchedalmond</c>: (255, 235, 205).</summary>
    public static ColorRgba BlanchedAlmond => new(255, 235, 205);

    /// <summary>CSS <c>blue</c>: (0, 0, 255).</summary>
    public static ColorRgba Blue => new(0, 0, 255);

    /// <summary>CSS <c>blueviolet</c>: (138, 43, 226).</summary>
    public static ColorRgba BlueViolet => new(138, 43, 226);

    /// <summary>CSS <c>brown</c>: (165, 42, 42).</summary>
    public static ColorRgba Brown => new(165, 42, 42);

    /// <summary>CSS <c>burlywood</c>: (222, 184, 135).</summary>
    public static ColorRgba BurlyWood => new(222, 184, 135);

    /// <summary>CSS <c>cadetblue</c>: (95, 158, 160).</summary>
    public static ColorRgba CadetBlue => new(95, 158, 160);

    /// <summary>CSS <c>chartreuse</c>: (127, 255, 0).</summary>
    public static ColorRgba Chartreuse => new(127, 255, 0);

    /// <summary>CSS <c>chocolate</c>: (210, 105, 30).</summary>
    public static ColorRgba Chocolate => new(210, 105, 30);

    /// <summary>CSS <c>coral</c>: (255, 127, 80).</summary>
    public static ColorRgba Coral => new(255, 127, 80);

    /// <summary>CSS <c>cornflowerblue</c>: (100, 149, 237).</summary>
    public static ColorRgba CornflowerBlue => new(100, 149, 237);

    /// <summary>CSS <c>cornsilk</c>: (255, 248, 220).</summary>
    public static ColorRgba Cornsilk => new(255, 248, 220);

    /// <summary>CSS <c>crimson</c>: (220, 20, 60).</summary>
    public static ColorRgba Crimson => new(220, 20, 60);

    /// <summary>CSS <c>cyan</c>: (0, 255, 255).</summary>
    public static ColorRgba Cyan => new(0, 255, 255);

    /// <summary>CSS <c>darkblue</c>: (0, 0, 139).</summary>
    public static ColorRgba DarkBlue => new(0, 0, 139);

    /// <summary>CSS <c>darkcyan</c>: (0, 139, 139).</summary>
    public static ColorRgba DarkCyan => new(0, 139, 139);

    /// <summary>CSS <c>darkgoldenrod</c>: (184, 134, 11).</summary>
    public static ColorRgba DarkGoldenrod => new(184, 134, 11);

    /// <summary>CSS <c>darkgray</c>: (169, 169, 169).</summary>
    public static ColorRgba DarkGray => new(169, 169, 169);

    /// <summary>CSS <c>darkgreen</c>: (0, 100, 0).</summary>
    public static ColorRgba DarkGreen => new(0, 100, 0);

    /// <summary>CSS <c>darkgrey</c>: (169, 169, 169).</summary>
    public static ColorRgba DarkGrey => new(169, 169, 169);

    /// <summary>CSS <c>darkkhaki</c>: (189, 183, 107).</summary>
    public static ColorRgba DarkKhaki => new(189, 183, 107);

    /// <summary>CSS <c>darkmagenta</c>: (139, 0, 139).</summary>
    public static ColorRgba DarkMagenta => new(139, 0, 139);

    /// <summary>CSS <c>darkolivegreen</c>: (85, 107, 47).</summary>
    public static ColorRgba DarkOliveGreen => new(85, 107, 47);

    /// <summary>CSS <c>darkorange</c>: (255, 140, 0).</summary>
    public static ColorRgba DarkOrange => new(255, 140, 0);

    /// <summary>CSS <c>darkorchid</c>: (153, 50, 204).</summary>
    public static ColorRgba DarkOrchid => new(153, 50, 204);

    /// <summary>CSS <c>darkred</c>: (139, 0, 0).</summary>
    public static ColorRgba DarkRed => new(139, 0, 0);

    /// <summary>CSS <c>darksalmon</c>: (233, 150, 122).</summary>
    public static ColorRgba DarkSalmon => new(233, 150, 122);

    /// <summary>CSS <c>darkseagreen</c>: (143, 188, 143).</summary>
    public static ColorRgba DarkSeaGreen => new(143, 188, 143);

    /// <summary>CSS <c>darkslateblue</c>: (72, 61, 139).</summary>
    public static ColorRgba DarkSlateBlue => new(72, 61, 139);

    /// <summary>CSS <c>darkslategray</c>: (47, 79, 79).</summary>
    public static ColorRgba DarkSlateGray => new(47, 79, 79);

    /// <summary>CSS <c>darkslategrey</c>: (47, 79, 79).</summary>
    public static ColorRgba DarkSlateGrey => new(47, 79, 79);

    /// <summary>CSS <c>darkturquoise</c>: (0, 206, 209).</summary>
    public static ColorRgba DarkTurquoise => new(0, 206, 209);

    /// <summary>CSS <c>darkviolet</c>: (148, 0, 211).</summary>
    public static ColorRgba DarkViolet => new(148, 0, 211);

    /// <summary>CSS <c>deeppink</c>: (255, 20, 147).</summary>
    public static ColorRgba DeepPink => new(255, 20, 147);

    /// <summary>CSS <c>deepskyblue</c>: (0, 191, 255).</summary>
    public static ColorRgba DeepSkyBlue => new(0, 191, 255);

    /// <summary>CSS <c>dimgray</c>: (105, 105, 105).</summary>
    public static ColorRgba DimGray => new(105, 105, 105);

    /// <summary>CSS <c>dimgrey</c>: (105, 105, 105).</summary>
    public static ColorRgba DimGrey => new(105, 105, 105);

    /// <summary>CSS <c>dodgerblue</c>: (30, 144, 255).</summary>
    public static ColorRgba DodgerBlue => new(30, 144, 255);

    /// <summary>CSS <c>firebrick</c>: (178, 34, 34).</summary>
    public static ColorRgba Firebrick => new(178, 34, 34);

    /// <summary>CSS <c>floralwhite</c>: (255, 250, 240).</summary>
    public static ColorRgba FloralWhite => new(255, 250, 240);

    /// <summary>CSS <c>forestgreen</c>: (34, 139, 34).</summary>
    public static ColorRgba ForestGreen => new(34, 139, 34);

    /// <summary>CSS <c>fuchsia</c>: (255, 0, 255).</summary>
    public static ColorRgba Fuchsia => new(255, 0, 255);

    /// <summary>CSS <c>gainsboro</c>: (220, 220, 220).</summary>
    public static ColorRgba Gainsboro => new(220, 220, 220);

    /// <summary>CSS <c>ghostwhite</c>: (248, 248, 255).</summary>
    public static ColorRgba GhostWhite => new(248, 248, 255);

    /// <summary>CSS <c>gold</c>: (255, 215, 0).</summary>
    public static ColorRgba Gold => new(255, 215, 0);

    /// <summary>CSS <c>goldenrod</c>: (218, 165, 32).</summary>
    public static ColorRgba Goldenrod => new(218, 165, 32);

    /// <summary>CSS <c>gray</c>: (128, 128, 128).</summary>
    public static ColorRgba Gray => new(128, 128, 128);

    /// <summary>CSS <c>green</c>: (0, 128, 0).</summary>
    public static ColorRgba Green => new(0, 128, 0);

    /// <summary>CSS <c>greenyellow</c>: (173, 255, 47).</summary>
    public static ColorRgba GreenYellow => new(173, 255, 47);

    /// <summary>CSS <c>grey</c>: (128, 128, 128).</summary>
    public static ColorRgba Grey => new(128, 128, 128);

    /// <summary>CSS <c>honeydew</c>: (240, 255, 240).</summary>
    public static ColorRgba Honeydew => new(240, 255, 240);

    /// <summary>CSS <c>hotpink</c>: (255, 105, 180).</summary>
    public static ColorRgba HotPink => new(255, 105, 180);

    /// <summary>CSS <c>indianred</c>: (205, 92, 92).</summary>
    public static ColorRgba IndianRed => new(205, 92, 92);

    /// <summary>CSS <c>indigo</c>: (75, 0, 130).</summary>
    public static ColorRgba Indigo => new(75, 0, 130);

    /// <summary>CSS <c>ivory</c>: (255, 255, 240).</summary>
    public static ColorRgba Ivory => new(255, 255, 240);

    /// <summary>CSS <c>khaki</c>: (240, 230, 140).</summary>
    public static ColorRgba Khaki => new(240, 230, 140);

    /// <summary>CSS <c>lavender</c>: (230, 230, 250).</summary>
    public static ColorRgba Lavender => new(230, 230, 250);

    /// <summary>CSS <c>lavenderblush</c>: (255, 240, 245).</summary>
    public static ColorRgba LavenderBlush => new(255, 240, 245);

    /// <summary>CSS <c>lawngreen</c>: (124, 252, 0).</summary>
    public static ColorRgba LawnGreen => new(124, 252, 0);

    /// <summary>CSS <c>lemonchiffon</c>: (255, 250, 205).</summary>
    public static ColorRgba LemonChiffon => new(255, 250, 205);

    /// <summary>CSS <c>lightblue</c>: (173, 216, 230).</summary>
    public static ColorRgba LightBlue => new(173, 216, 230);

    /// <summary>CSS <c>lightcoral</c>: (240, 128, 128).</summary>
    public static ColorRgba LightCoral => new(240, 128, 128);

    /// <summary>CSS <c>lightcyan</c>: (224, 255, 255).</summary>
    public static ColorRgba LightCyan => new(224, 255, 255);

    /// <summary>CSS <c>lightgoldenrodyellow</c>: (250, 250, 210).</summary>
    public static ColorRgba LightGoldenrodYellow => new(250, 250, 210);

    /// <summary>CSS <c>lightgray</c>: (211, 211, 211).</summary>
    public static ColorRgba LightGray => new(211, 211, 211);

    /// <summary>CSS <c>lightgreen</c>: (144, 238, 144).</summary>
    public static ColorRgba LightGreen => new(144, 238, 144);

    /// <summary>CSS <c>lightgrey</c>: (211, 211, 211).</summary>
    public static ColorRgba LightGrey => new(211, 211, 211);

    /// <summary>CSS <c>lightpink</c>: (255, 182, 193).</summary>
    public static ColorRgba LightPink => new(255, 182, 193);

    /// <summary>CSS <c>lightsalmon</c>: (255, 160, 122).</summary>
    public static ColorRgba LightSalmon => new(255, 160, 122);

    /// <summary>CSS <c>lightseagreen</c>: (32, 178, 170).</summary>
    public static ColorRgba LightSeaGreen => new(32, 178, 170);

    /// <summary>CSS <c>lightskyblue</c>: (135, 206, 250).</summary>
    public static ColorRgba LightSkyBlue => new(135, 206, 250);

    /// <summary>CSS <c>lightslategray</c>: (119, 136, 153).</summary>
    public static ColorRgba LightSlateGray => new(119, 136, 153);

    /// <summary>CSS <c>lightslategrey</c>: (119, 136, 153).</summary>
    public static ColorRgba LightSlateGrey => new(119, 136, 153);

    /// <summary>CSS <c>lightsteelblue</c>: (176, 196, 222).</summary>
    public static ColorRgba LightSteelBlue => new(176, 196, 222);

    /// <summary>CSS <c>lightyellow</c>: (255, 255, 224).</summary>
    public static ColorRgba LightYellow => new(255, 255, 224);

    /// <summary>CSS <c>lime</c>: (0, 255, 0).</summary>
    public static ColorRgba Lime => new(0, 255, 0);

    /// <summary>CSS <c>limegreen</c>: (50, 205, 50).</summary>
    public static ColorRgba LimeGreen => new(50, 205, 50);

    /// <summary>CSS <c>linen</c>: (250, 240, 230).</summary>
    public static ColorRgba Linen => new(250, 240, 230);

    /// <summary>CSS <c>magenta</c>: (255, 0, 255).</summary>
    public static ColorRgba Magenta => new(255, 0, 255);

    /// <summary>CSS <c>maroon</c>: (128, 0, 0).</summary>
    public static ColorRgba Maroon => new(128, 0, 0);

    /// <summary>CSS <c>mediumaquamarine</c>: (102, 205, 170).</summary>
    public static ColorRgba MediumAquamarine => new(102, 205, 170);

    /// <summary>CSS <c>mediumblue</c>: (0, 0, 205).</summary>
    public static ColorRgba MediumBlue => new(0, 0, 205);

    /// <summary>CSS <c>mediumorchid</c>: (186, 85, 211).</summary>
    public static ColorRgba MediumOrchid => new(186, 85, 211);

    /// <summary>CSS <c>mediumpurple</c>: (147, 112, 219).</summary>
    public static ColorRgba MediumPurple => new(147, 112, 219);

    /// <summary>CSS <c>mediumseagreen</c>: (60, 179, 113).</summary>
    public static ColorRgba MediumSeaGreen => new(60, 179, 113);

    /// <summary>CSS <c>mediumslateblue</c>: (123, 104, 238).</summary>
    public static ColorRgba MediumSlateBlue => new(123, 104, 238);

    /// <summary>CSS <c>mediumspringgreen</c>: (0, 250, 154).</summary>
    public static ColorRgba MediumSpringGreen => new(0, 250, 154);

    /// <summary>CSS <c>mediumturquoise</c>: (72, 209, 204).</summary>
    public static ColorRgba MediumTurquoise => new(72, 209, 204);

    /// <summary>CSS <c>mediumvioletred</c>: (199, 21, 133).</summary>
    public static ColorRgba MediumVioletRed => new(199, 21, 133);

    /// <summary>CSS <c>midnightblue</c>: (25, 25, 112).</summary>
    public static ColorRgba MidnightBlue => new(25, 25, 112);

    /// <summary>CSS <c>mintcream</c>: (245, 255, 250).</summary>
    public static ColorRgba MintCream => new(245, 255, 250);

    /// <summary>CSS <c>mistyrose</c>: (255, 228, 225).</summary>
    public static ColorRgba MistyRose => new(255, 228, 225);

    /// <summary>CSS <c>moccasin</c>: (255, 228, 181).</summary>
    public static ColorRgba Moccasin => new(255, 228, 181);

    /// <summary>CSS <c>navajowhite</c>: (255, 222, 173).</summary>
    public static ColorRgba NavajoWhite => new(255, 222, 173);

    /// <summary>CSS <c>navy</c>: (0, 0, 128).</summary>
    public static ColorRgba Navy => new(0, 0, 128);

    /// <summary>CSS <c>oldlace</c>: (253, 245, 230).</summary>
    public static ColorRgba OldLace => new(253, 245, 230);

    /// <summary>CSS <c>olive</c>: (128, 128, 0).</summary>
    public static ColorRgba Olive => new(128, 128, 0);

    /// <summary>CSS <c>olivedrab</c>: (107, 142, 35).</summary>
    public static ColorRgba OliveDrab => new(107, 142, 35);

    /// <summary>CSS <c>orange</c>: (255, 165, 0).</summary>
    public static ColorRgba Orange => new(255, 165, 0);

    /// <summary>CSS <c>orangered</c>: (255, 69, 0).</summary>
    public static ColorRgba OrangeRed => new(255, 69, 0);

    /// <summary>CSS <c>orchid</c>: (218, 112, 214).</summary>
    public static ColorRgba Orchid => new(218, 112, 214);

    /// <summary>CSS <c>palegoldenrod</c>: (238, 232, 170).</summary>
    public static ColorRgba PaleGoldenrod => new(238, 232, 170);

    /// <summary>CSS <c>palegreen</c>: (152, 251, 152).</summary>
    public static ColorRgba PaleGreen => new(152, 251, 152);

    /// <summary>CSS <c>paleturquoise</c>: (175, 238, 238).</summary>
    public static ColorRgba PaleTurquoise => new(175, 238, 238);

    /// <summary>CSS <c>palevioletred</c>: (219, 112, 147).</summary>
    public static ColorRgba PaleVioletRed => new(219, 112, 147);

    /// <summary>CSS <c>papayawhip</c>: (255, 239, 213).</summary>
    public static ColorRgba PapayaWhip => new(255, 239, 213);

    /// <summary>CSS <c>peachpuff</c>: (255, 218, 185).</summary>
    public static ColorRgba PeachPuff => new(255, 218, 185);

    /// <summary>CSS <c>peru</c>: (205, 133, 63).</summary>
    public static ColorRgba Peru => new(205, 133, 63);

    /// <summary>CSS <c>pink</c>: (255, 192, 203).</summary>
    public static ColorRgba Pink => new(255, 192, 203);

    /// <summary>CSS <c>plum</c>: (221, 160, 221).</summary>
    public static ColorRgba Plum => new(221, 160, 221);

    /// <summary>CSS <c>powderblue</c>: (176, 224, 230).</summary>
    public static ColorRgba PowderBlue => new(176, 224, 230);

    /// <summary>CSS <c>purple</c>: (128, 0, 128).</summary>
    public static ColorRgba Purple => new(128, 0, 128);

    /// <summary>CSS <c>rebeccapurple</c>: (102, 51, 153).</summary>
    public static ColorRgba RebeccaPurple => new(102, 51, 153);

    /// <summary>CSS <c>red</c>: (255, 0, 0).</summary>
    public static ColorRgba Red => new(255, 0, 0);

    /// <summary>CSS <c>rosybrown</c>: (188, 143, 143).</summary>
    public static ColorRgba RosyBrown => new(188, 143, 143);

    /// <summary>CSS <c>royalblue</c>: (65, 105, 225).</summary>
    public static ColorRgba RoyalBlue => new(65, 105, 225);

    /// <summary>CSS <c>saddlebrown</c>: (139, 69, 19).</summary>
    public static ColorRgba SaddleBrown => new(139, 69, 19);

    /// <summary>CSS <c>salmon</c>: (250, 128, 114).</summary>
    public static ColorRgba Salmon => new(250, 128, 114);

    /// <summary>CSS <c>sandybrown</c>: (244, 164, 96).</summary>
    public static ColorRgba SandyBrown => new(244, 164, 96);

    /// <summary>CSS <c>seagreen</c>: (46, 139, 87).</summary>
    public static ColorRgba SeaGreen => new(46, 139, 87);

    /// <summary>CSS <c>seashell</c>: (255, 245, 238).</summary>
    public static ColorRgba SeaShell => new(255, 245, 238);

    /// <summary>CSS <c>sienna</c>: (160, 82, 45).</summary>
    public static ColorRgba Sienna => new(160, 82, 45);

    /// <summary>CSS <c>silver</c>: (192, 192, 192).</summary>
    public static ColorRgba Silver => new(192, 192, 192);

    /// <summary>CSS <c>skyblue</c>: (135, 206, 235).</summary>
    public static ColorRgba SkyBlue => new(135, 206, 235);

    /// <summary>CSS <c>slateblue</c>: (106, 90, 205).</summary>
    public static ColorRgba SlateBlue => new(106, 90, 205);

    /// <summary>CSS <c>slategray</c>: (112, 128, 144).</summary>
    public static ColorRgba SlateGray => new(112, 128, 144);

    /// <summary>CSS <c>slategrey</c>: (112, 128, 144).</summary>
    public static ColorRgba SlateGrey => new(112, 128, 144);

    /// <summary>CSS <c>snow</c>: (255, 250, 250).</summary>
    public static ColorRgba Snow => new(255, 250, 250);

    /// <summary>CSS <c>springgreen</c>: (0, 255, 127).</summary>
    public static ColorRgba SpringGreen => new(0, 255, 127);

    /// <summary>CSS <c>steelblue</c>: (70, 130, 180).</summary>
    public static ColorRgba SteelBlue => new(70, 130, 180);

    /// <summary>CSS <c>tan</c>: (210, 180, 140).</summary>
    public static ColorRgba Tan => new(210, 180, 140);

    /// <summary>CSS <c>teal</c>: (0, 128, 128).</summary>
    public static ColorRgba Teal => new(0, 128, 128);

    /// <summary>CSS <c>thistle</c>: (216, 191, 216).</summary>
    public static ColorRgba Thistle => new(216, 191, 216);

    /// <summary>CSS <c>tomato</c>: (255, 99, 71).</summary>
    public static ColorRgba Tomato => new(255, 99, 71);

    /// <summary>CSS <c>turquoise</c>: (64, 224, 208).</summary>
    public static ColorRgba Turquoise => new(64, 224, 208);

    /// <summary>CSS <c>violet</c>: (238, 130, 238).</summary>
    public static ColorRgba Violet => new(238, 130, 238);

    /// <summary>CSS <c>wheat</c>: (245, 222, 179).</summary>
    public static ColorRgba Wheat => new(245, 222, 179);

    /// <summary>CSS <c>white</c>: (255, 255, 255).</summary>
    public static ColorRgba White => new(255, 255, 255);

    /// <summary>CSS <c>whitesmoke</c>: (245, 245, 245).</summary>
    public static ColorRgba WhiteSmoke => new(245, 245, 245);

    /// <summary>CSS <c>yellow</c>: (255, 255, 0).</summary>
    public static ColorRgba Yellow => new(255, 255, 0);

    /// <summary>CSS <c>yellowgreen</c>: (154, 205, 50).</summary>
    public static ColorRgba YellowGreen => new(154, 205, 50);
}
