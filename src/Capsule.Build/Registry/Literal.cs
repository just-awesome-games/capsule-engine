using System.Globalization;
using System.Text;

namespace Capsule.Build.Registry;

/// <summary>C# literals, the same on every machine whatever its culture.</summary>
internal static class Literal
{
    /// <summary>Escapes what a C# string literal cannot hold as written, a line break included.</summary>
    internal static string Of(string value)
    {
        StringBuilder literal = new(value.Length + 2);
        literal.Append('"');
        foreach (char character in value)
        {
            _ = character switch
            {
                '\\' => literal.Append(@"\\"),
                '"' => literal.Append("\\\""),
                _ when char.IsControl(character) || character is '\u2028' or '\u2029' =>
                    literal.Append(@"\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture)),
                _ => literal.Append(character),
            };
        }

        return literal.Append('"').ToString();
    }

    internal static string Of(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Round-trippable and suffixed, since an unsuffixed literal is a double a float parameter cannot take.</summary>
    internal static string Of(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "F";

    /// <summary>Round-trippable, so the constructor takes back the double the build measured.</summary>
    internal static string Of(double value) => Number(value) + "D";

    /// <summary>A number as a summary states it, unsuffixed.</summary>
    internal static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
