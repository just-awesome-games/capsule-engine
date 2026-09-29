namespace Capsule.Bench;

// One assets run as committed under results/assets/: the suite's machine and build header, the corpus's
// version and size, then one row per case.
internal sealed record AssetsRecord(
    string Timestamp,
    string Label,
    string Commit,
    string Configuration,
    string Os,
    string Cpu,
    string Gpu,
    string EngineVersion,
    int CorpusVersion,
    int CorpusFiles,
    long CorpusBytes,
    IReadOnlyList<AssetCaseRecord> Cases);

// Medians over the case's runs, in milliseconds: the whole `dotnet build`, and the CapsuleRunBuildTool
// target within it.
internal sealed record AssetCaseRecord(string Case, double WallMs, double ToolMs, int Runs);
