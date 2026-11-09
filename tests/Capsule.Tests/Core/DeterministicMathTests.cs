using System.Numerics;
using System.Text.RegularExpressions;
using Capsule.Tests.Packaging;

namespace Capsule.Tests.Core;

// The platform's own sine and exponential are correctly rounded by no standard. Simulation code
// may not call them. These are the replacements: what is asserted is that they are accurate enough
// to stand in for the real functions over the range they claim, and honest about where that ends,
// since being identical everywhere is what they are built of.
public sealed partial class DeterministicMathTests
{
    // Over the inputs LerpDigest walks. The NativeAOT smoke program asserts the same digest, and a build or a
    // processor that fused these multiplies would move it.
    private const ulong ExpectedLerpDigest = 0x192EB20C26DCD9D9UL;

    // The bound every member publishes: absolute for a sine, relative for the exponential. Neither
    // is correctly rounded. This is about a unit in the last place rather than half of one.
    private const double Bound = 6e-8;

    // The magnitude sine and cosine claim their accuracy out to.
    private const float SineRange = 16777216f;

    [Fact]
    public void Lerp_IsPlainArithmeticOnEveryBuild() => Assert.Equal(ExpectedLerpDigest, LerpDigest());

    // Within reach the result is the target's own bits, never one rounded short or past it.
    [Theory]
    [InlineData(0.1f, 0.3f, 0.2f, 0.3f)]
    [InlineData(0.3f, 0.1f, 1f, 0.1f)]
    [InlineData(1f, 5f, 1.5f, 2.5f)]
    [InlineData(5f, 1f, 1.5f, 3.5f)]
    [InlineData(1f, 5f, 0f, 1f)]
    [InlineData(5f, 5f, 0f, 5f)]
    public void MovingTowardATarget_StepsByTheMostAllowedAndLandsOnItExactly(float current, float target, float maxDelta, float expected)
    {
        Assert.Equal(expected, DeterministicMath.MoveToward(current, target, maxDelta));
        Assert.Equal(new Vector2(expected, current), DeterministicMath.MoveToward(new Vector2(current, current), new Vector2(target, current), maxDelta));
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.NaN)]
    public void MovingTowardATarget_PassesANaNThrough(float current, float target)
    {
        Assert.True(float.IsNaN(DeterministicMath.MoveToward(current, target, 0.5f)));

        Vector2 moved = DeterministicMath.MoveToward(new Vector2(current, 0f), new Vector2(target, 0f), 0.5f);
        Assert.True(float.IsNaN(moved.X));
    }

    // A point moves along the line to its target, as far diagonally as along an axis.
    [Fact]
    public void MovingAPointTowardATarget_TravelsTheDistanceAlongTheLine()
    {
        Vector2 target = new(0.3f, -0.7f);

        Assert.Equal(new Vector2(3f, 4f), DeterministicMath.MoveToward(Vector2.Zero, new Vector2(6f, 8f), 5f));
        Assert.Equal(target, DeterministicMath.MoveToward(new Vector2(0.1f, 0.2f), target, 1f));
        Assert.Equal(new Vector2(0.1f, 0.2f), DeterministicMath.MoveToward(new Vector2(0.1f, 0.2f), target, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicMath.MoveToward(0f, 1f, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => DeterministicMath.MoveToward(Vector2.Zero, Vector2.One, -1f));
    }

    // A Lerp whose multiplies fuse lands a last bit away from one whose multiplies round. The analyzer holds game logic
    // to this, and nothing but this scan holds the engine's own simulation.
    [Fact]
    public void NoSimulationSource_CallsMathThatDiffersBetweenMachines()
    {
        string root = ProseTests.RepositoryRoot();
        string[] found =
        [
            .. new[] { "Capsule.Core", "Capsule.Physics", "Capsule.Scenes" }
                .SelectMany(module => Directory.EnumerateFiles(Path.Combine(root, "src", module), "*.cs", SearchOption.AllDirectories))
                .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .SelectMany(path => File.ReadLines(path)
                    .Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && MachineDependentMath().IsMatch(line))
                    .Select(line => $"{Path.GetRelativePath(root, path)}: {line.Trim()}")),
        ];

        Assert.True(found.Length == 0, $"These lines round differently between machines. Call DeterministicMath.Lerp or write plain arithmetic:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    private static ulong LerpDigest()
    {
        ulong digest = 14695981039346656037UL;
        uint state = 1;
        for (int sample = 0; sample < 20_000; sample++)
        {
            Vector2 from = new(Next(ref state), Next(ref state));
            Vector2 to = new(Next(ref state), Next(ref state));
            Vector2 point = DeterministicMath.Lerp(from, to, Next(ref state) / 1000f);
            digest = (digest ^ BitConverter.SingleToUInt32Bits(point.X)) * 1099511628211UL;
            digest = (digest ^ BitConverter.SingleToUInt32Bits(point.Y)) * 1099511628211UL;
        }

        return digest;

        static float Next(ref uint state)
        {
            state = (state * 1664525u) + 1013904223u;
            return (state >> 8) / 16777.216f;
        }
    }

    [GeneratedRegex(@"\b(Vector[234]|Matrix3x2|Matrix4x4|Quaternion|Plane|float|double|Single|Double|MathF|Math)\.(Lerp|Transform(?![^;]*Quaternion)|TransformNormal|Reflect|MultiplyAddEstimate|ReciprocalEstimate|ReciprocalSqrtEstimate)\b")]
    private static partial Regex MachineDependentMath();

    [Theory]
    [InlineData(nameof(DeterministicMath.Sin))]
    [InlineData(nameof(DeterministicMath.Cos))]
    public void ASineOrCosineOverTheRangeItClaims_IsWithinThePublishedBound(string name)
    {
        (Func<float, float> Subject, Func<double, double> Reference) function = name switch
        {
            nameof(DeterministicMath.Sin) => (DeterministicMath.Sin, Math.Sin),
            nameof(DeterministicMath.Cos) => (DeterministicMath.Cos, Math.Cos),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No case for this function."),
        };

        double worst = 0.0;
        void Check(float radians) => worst = Math.Max(worst, Math.Abs(function.Subject(radians) - function.Reference(radians)));

        // Dense over the first turns, where a curve or an oscillator actually lives.
        for (int sample = -200_000; sample <= 200_000; sample++)
        {
            Check(sample / 10_000f);
        }

        // Then coarse the whole way out to the claimed magnitude, on a step that is no multiple of a
        // turn. The fold is sampled at every phase rather than at one.
        for (int sample = -1_000_000; sample <= 1_000_000; sample++)
        {
            Check((float)(sample * 16.777216));
        }

        // The fold subtracts a whole number of turns, and it is the last turn that has the least of the
        // argument's own precision left to spend on it.
        foreach (float radians in new[] { SineRange, -SineRange, SineRange - 1f, SineRange - 0.5f, SineRange / 2f })
        {
            Check(radians);
        }

        Assert.True(worst < Bound, $"{name} is out by {worst}.");
    }

    [Fact]
    public void TwoRaisedToAnExponent_IsWithinThePublishedBoundRelatively()
    {
        double worst = 0.0;

        for (int sample = -126_000; sample <= 127_000; sample++)
        {
            float exponent = sample / 1_000f;
            double expected = Math.Pow(2.0, exponent);
            worst = Math.Max(worst, Math.Abs((DeterministicMath.Exp2(exponent) - expected) / expected));
        }

        Assert.True(worst < Bound, $"the exponential is out by {worst} relatively.");
    }

    // The whole part is a power of two and scaling by it is exact. A whole exponent is not an
    // approximation at all, subnormals included.
    [Fact]
    public void EveryWholeExponent_IsExact()
    {
        for (int power = -149; power <= 127; power++)
        {
            Assert.Equal(MathF.ScaleB(1f, power), DeterministicMath.Exp2(power));
        }
    }

    // The last exponent a float still holds something finite for: 2^128 is past it, 2^127.5 is not.
    [Fact]
    public void AnExponentBetweenTheLastWholeOneAndTheEnd_IsFinite()
    {
        double expected = Math.Pow(2.0, 127.5);
        float highest = DeterministicMath.Exp2(127.5f);

        Assert.True(float.IsFinite(highest), "2^127.5 is inside what a float holds.");
        Assert.True(Math.Abs((highest - expected) / expected) < Bound, $"2^127.5 is out by {(highest - expected) / expected}.");
    }

    [Fact]
    public void AFractionalExponentUnderTheNormalRange_IsTheNearestSubnormal()
    {
        // Counted in smallest subnormals, 2^-147.5 is 2.83 of them, 2^-148.5 is 1.41, 2^-149.9 is
        // 0.54 and 2^-150 is half of one, which ties to even. Rounded, never flushed.
        Assert.Equal(BitConverter.Int32BitsToSingle(3), DeterministicMath.Exp2(-147.5f));
        Assert.Equal(BitConverter.Int32BitsToSingle(1), DeterministicMath.Exp2(-148.5f));
        Assert.Equal(BitConverter.Int32BitsToSingle(1), DeterministicMath.Exp2(-149.9f));
        Assert.Equal(0f, DeterministicMath.Exp2(-150f));
    }

    [Theory]
    [InlineData(128f, float.PositiveInfinity)]
    [InlineData(200f, float.PositiveInfinity)]
    [InlineData(float.PositiveInfinity, float.PositiveInfinity)]
    [InlineData(-151f, 0f)]
    [InlineData(-200f, 0f)]
    [InlineData(float.NegativeInfinity, 0f)]
    public void AnExponentAtTheEndsOfWhatAFloatHolds_IsWhatThatEndRoundsTo(float exponent, float expected)
    {
        Assert.Equal(expected, DeterministicMath.Exp2(exponent));
    }

    // The arctangent's result reaches pi, where half a float's step is just under 1.2e-7.
    [Fact]
    public void AnAngleToAPoint_IsWithinItsPublishedBound()
    {
        const double AngleBound = 1.2e-7;
        double worst = 0.0;

        // Every direction around the circle, at radii from a thousandth of a unit to a million. The
        // fold about a sixth of pi and the swap about the diagonal are crossed in all four quadrants.
        foreach (float radius in new[] { 0.001f, 1f, 37.5f, 1_000_000f })
        {
            for (int sample = 0; sample < 200_000; sample++)
            {
                double turn = sample * (2.0 * Math.PI / 200_000);
                float x = (float)(radius * Math.Cos(turn));
                float y = (float)(radius * Math.Sin(turn));
                worst = Math.Max(worst, Math.Abs(DeterministicMath.Atan2(y, x) - Math.Atan2(y, x)));
            }
        }

        Assert.True(worst < AngleBound, $"the arctangent is out by {worst}.");
    }

    // Dense over the span a game calls each function in, then at every 2003rd float bit pattern of either
    // sign across the whole range it claims. Every binade is sampled.
    [Theory]
    [InlineData(nameof(DeterministicMath.Tan))]
    [InlineData(nameof(DeterministicMath.Atan))]
    [InlineData(nameof(DeterministicMath.Asin))]
    [InlineData(nameof(DeterministicMath.Acos))]
    [InlineData(nameof(DeterministicMath.Exp))]
    [InlineData(nameof(DeterministicMath.Log2))]
    [InlineData(nameof(DeterministicMath.Log))]
    [InlineData(nameof(DeterministicMath.Log10))]
    public void AFunctionOverTheRangeItClaims_IsWithinThePublishedBoundRelatively(string name)
    {
        // Exp's range is where its result is a normal float.
        (Func<float, float> Subject, Func<double, double> Reference, float Low, float High, float DenseLow, float DenseHigh) span = name switch
        {
            nameof(DeterministicMath.Tan) => (DeterministicMath.Tan, Math.Tan, -SineRange, SineRange, -10f, 10f),
            nameof(DeterministicMath.Atan) => (DeterministicMath.Atan, Math.Atan, float.MinValue, float.MaxValue, -100f, 100f),
            nameof(DeterministicMath.Asin) => (DeterministicMath.Asin, Math.Asin, -1f, 1f, -1f, 1f),
            nameof(DeterministicMath.Acos) => (DeterministicMath.Acos, Math.Acos, -1f, 1f, -1f, 1f),
            nameof(DeterministicMath.Exp) => (DeterministicMath.Exp, Math.Exp, -87.3f, 88.7f, -20f, 20f),
            nameof(DeterministicMath.Log2) => (DeterministicMath.Log2, Math.Log2, float.Epsilon, float.MaxValue, 0f, 100f),
            nameof(DeterministicMath.Log) => (DeterministicMath.Log, Math.Log, float.Epsilon, float.MaxValue, 0f, 100f),
            nameof(DeterministicMath.Log10) => (DeterministicMath.Log10, Math.Log10, float.Epsilon, float.MaxValue, 0f, 100f),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No case for this function."),
        };

        double worst = 0.0;
        void Check(float value)
        {
            if (value >= span.Low && value <= span.High)
            {
                worst = Math.Max(worst, RelativeError(span.Subject(value), span.Reference(value)));
            }
        }

        for (int sample = 0; sample <= 400_000; sample++)
        {
            Check((float)(span.DenseLow + ((span.DenseHigh - span.DenseLow) * (sample / 400_000.0))));
        }

        for (int bits = 0; bits < 0x7F800000; bits += 2003)
        {
            float magnitude = BitConverter.Int32BitsToSingle(bits);
            Check(magnitude);
            Check(-magnitude);
        }

        Assert.True(worst < Bound, $"{name} is out by {worst} relatively.");
    }

    [Fact]
    public void APowerWhoseResultIsANormalFloat_IsWithinThePublishedBoundRelatively()
    {
        double worst = 0.0;
        void Check(float value, float power)
        {
            double expected = Math.Pow(value, power);
            if (Math.Abs(expected) >= MinNormal && Math.Abs(expected) <= float.MaxValue)
            {
                worst = Math.Max(worst, RelativeError(DeterministicMath.Pow(value, power), expected));
            }
        }

        // Bases a game raises, over fractional powers, and negative bases over whole ones.
        for (int step = 1; step <= 2_000; step++)
        {
            float value = step * 0.01f;
            for (int power = -600; power <= 600; power++)
            {
                Check(value, power * 0.05f);
            }

            for (int power = -30; power <= 30; power++)
            {
                Check(-value, power);
            }
        }

        // Every binade of base, and bases a few steps from one under powers large enough to amplify the
        // logarithm's error into the result.
        for (int bits = 1; bits < 0x7F800000; bits += 20011)
        {
            foreach (float power in new[] { -2.5f, -1f, -0.5f, 0.3f, 1f, 1.7f })
            {
                Check(BitConverter.Int32BitsToSingle(bits), power);
            }
        }

        for (int step = -1_000; step <= 1_000; step++)
        {
            Check(1f + (step * 1.1920929e-7f), 1e6f + (step * 37f));
        }

        Assert.True(worst < Bound, $"the power is out by {worst} relatively.");
    }

    private const double MinNormal = 1.1754943508222875e-38;

    private static double RelativeError(float actual, double expected) =>
        expected == 0.0 ? Math.Abs(actual) : Math.Abs((actual - expected) / expected);
}
