using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capsule.Bench;

// One suite run as committed under results/: the machine and build, then one row of one shape per
// workload. Property order is declaration order and names are camel-cased, so a diff reads and a
// trend script reads one schema.
internal sealed record SuiteRecord(
    string Timestamp,
    string Label,
    bool Uncapped,
    string Commit,
    string Configuration,
    string Os,
    string Cpu,
    string Gpu,
    string EngineVersion,
    IReadOnlyList<WorkloadRecord> Workloads)
{
    private static readonly JsonSerializerOptions Options = new(SuiteRecordJson.Default.Options)
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, Options);
    }
}

// Headless rows carry steps and stepMs; windowed rows frames, drawMs and intervalMs; the rest is
// null. gen0Collections counts inside the measured steps or frames alone.
internal sealed record WorkloadRecord(
    string Name,
    string Mode,
    string Surface,
    int? Steps,
    int? Frames,
    DrawTiming? DrawMs,
    StepTiming? StepMs,
    IntervalTiming? IntervalMs,
    int Gen0Collections,
    string? CaptureSha256);

// Milliseconds the host spent submitting the game frame: FrameRenderer.Draw alone.
internal sealed record DrawTiming(double Median, double P95, double Max);

// Milliseconds per fixed step, each step timed on its own.
internal sealed record StepTiming(double Median, double P95);

// Milliseconds from one frame's start to the next: the display's rate when the host keeps up, and
// in its tail the hitches the median hides. Uncapped, it is the host's true frame cost.
internal sealed record IntervalTiming(double Median, double P95, double Max);

[JsonSerializable(typeof(SuiteRecord))]
internal sealed partial class SuiteRecordJson : JsonSerializerContext;
