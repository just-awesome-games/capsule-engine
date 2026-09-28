namespace Capsule.Assets;

// The length a format's JSON Schema states for a string or an array member. Only the schema
// generator reads it, and each reader enforces its own rules. It stands in for the DataAnnotations
// length attributes, whose constructors a trimmed publish refuses.
[AttributeUsage(AttributeTargets.Property)]
internal sealed class SchemaLengthAttribute : Attribute
{
    internal int Minimum { get; }

    // int.MaxValue states no upper bound.
    internal int Maximum { get; }

    internal SchemaLengthAttribute(int minimum, int maximum = int.MaxValue)
    {
        Minimum = minimum;
        Maximum = maximum;
    }
}
