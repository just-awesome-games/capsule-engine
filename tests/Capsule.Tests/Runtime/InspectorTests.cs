using System.Globalization;
using System.Numerics;
using Capsule.Diagnostics;

namespace Capsule.Tests.Runtime;

public sealed class InspectorTests
{
    private enum Mood
    {
        Idle,
        Alert,
    }

    // Written under a culture whose decimal separator is a comma, so the invariant spelling is
    // what the assertions prove; a float is not widened to double on the way through.
    [Fact]
    public void Fields_FormatInvariantAtShortestRoundTripAndNameEnumsAndNulls()
    {
        CultureInfo was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Inspector inspector = new();

            inspector.Field("Speed", 0.1f);
            inspector.Field("Ratio", 0.1);
            inspector.Field("Count", 12);
            inspector.Field("At", new Vector2(120f, 64.5f));
            inspector.Field("Mood", Mood.Alert);
            inspector.Field("Target", (string?)null);
            inspector.Field("Grounded", true);

            Assert.Equal(
                [
                    ("Speed", "0.1"),
                    ("Ratio", "0.1"),
                    ("Count", "12"),
                    ("At", "(120, 64.5)"),
                    ("Mood", "Alert"),
                    ("Target", "null"),
                    ("Grounded", "True"),
                ],
                Rows(inspector));
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void ASection_IsAHeadingRowAndClearDropsEverything()
    {
        Inspector inspector = new();

        inspector.Field("Health", 3);
        inspector.Section("Weapon");
        inspector.Field("Ammo", 7);

        InspectorRow[] rows = inspector.Rows.ToArray();
        Assert.Equal(3, rows.Length);
        Assert.False(rows[0].IsHeading);
        Assert.True(rows[1].IsHeading);
        Assert.Equal("Weapon", rows[1].Label);
        Assert.Null(rows[1].Value);

        inspector.Clear();

        Assert.True(inspector.Rows.IsEmpty);
    }

    private static (string Label, string? Value)[] Rows(Inspector inspector)
    {
        ReadOnlySpan<InspectorRow> rows = inspector.Rows;
        (string, string?)[] pairs = new (string, string?)[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            pairs[index] = (rows[index].Label, rows[index].Value);
        }

        return pairs;
    }
}
