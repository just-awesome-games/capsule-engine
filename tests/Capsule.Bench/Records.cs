using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Bench;

// Where records go and how they are written. Property order is declaration order and names are
// camel-cased, so a diff reads and a trend script reads one schema per command.
internal static class Records
{
    private static readonly JsonSerializerOptions Options = new(RecordJson.Default.Options)
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string Timestamp(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // Under results/ beside this source, then any subdirectory, named for the UTC time the run started.
    // Returns the record's path.
    internal static string Save<T>(T record, DateTime started, string subdirectory = "")
    {
        string path = Path.Combine(SourceDirectory(), "results", subdirectory, started.ToString("yyyy-MM-dd'T'HH-mm-ss'Z'", CultureInfo.InvariantCulture) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, record, Options);

        return path;
    }

    // Beside this source file wherever the bench is run from, so a record lands where it is committed.
    internal static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;
}

// Nearest-rank percentiles over the sorted values, rounded to microseconds. An even count's median
// is the mean of its two middle values.
internal readonly record struct Percentiles(double Median, double P95, double Max)
{
    internal static Percentiles Of(double[] values)
    {
        Array.Sort(values);

        int count = values.Length;
        double median = count % 2 == 1 ? values[count / 2] : (values[(count / 2) - 1] + values[count / 2]) / 2d;
        double p95 = values[Math.Max(0, (int)Math.Ceiling(0.95 * count) - 1)];

        return new Percentiles(Math.Round(median, 3), Math.Round(p95, 3), Math.Round(values[^1], 3));
    }
}

[JsonSerializable(typeof(SuiteRecord))]
[JsonSerializable(typeof(AssetsRecord))]
internal sealed partial class RecordJson : JsonSerializerContext;
