namespace Capsule.Bench;

// One suite run as committed under results/: the machine and build, then one row of one shape per
// workload. Lane is "all", or the one lane `--lane` named.
internal sealed record SuiteRecord(
    string Timestamp,
    string Label,
    string Lane,
    bool Uncapped,
    string Commit,
    string Configuration,
    string Os,
    string Cpu,
    string Gpu,
    string EngineVersion,
    IReadOnlyList<WorkloadRecord> Workloads);

// One workload's row. The README's "Reading a record" lists which columns each lane fills.
internal sealed record WorkloadRecord(
    string Name,
    string Mode,
    string Surface,
    int? Steps,
    int? Frames,
    Percentiles? DrawMs,
    StepTiming? StepMs,
    StepTiming? ViewMs,
    long? StepBytes,
    long? ViewBytes,
    Percentiles? IntervalMs,
    int Gen0Collections,
    string? CaptureSha256);

// Milliseconds per fixed step, or per frame built after one, each timed on its own.
internal sealed record StepTiming(double Median, double P95);
