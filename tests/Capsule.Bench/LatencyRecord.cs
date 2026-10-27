namespace Capsule.Bench;

// One latency run as committed under results/latency/: the suite's machine and build header, how the
// run was paced and pressed, then the presses measured. LatencyMs runs from each press event's SDL
// timestamp to the present of the first frame whose steps consumed it. InjectedLatencyMs runs from the
// injector's own clock instead, paired press for press, and is null when a press went unmeasured.
internal sealed record LatencyRecord(
    string Timestamp,
    string Label,
    string Commit,
    string Configuration,
    bool NativeAot,
    string Os,
    string Cpu,
    string Gpu,
    string EngineVersion,
    string Workload,
    bool VSync,
    int Cap,
    string Injection,
    int Injected,
    int Presses,
    Percentiles LatencyMs,
    Percentiles? InjectedLatencyMs);
