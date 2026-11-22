using System.Text.RegularExpressions;
using Capsule.Tests.Packaging;

namespace Capsule.Tests.Allocation;

public sealed partial class MeasuredWindowTests
{
    // A window opened by reading the allocation counter directly can be charged bytes nothing allocated, and fails only
    // under the parallel suite.
    [Fact]
    public void EveryMeasuredWindow_OpensThroughStart()
    {
        string root = ProseTests.RepositoryRoot();
        string suite = Path.Combine(root, "tests", "Capsule.Tests");
        string[] found =
        [
            .. Directory.EnumerateFiles(suite, "*.cs", SearchOption.AllDirectories)
                .Where(static path => Path.GetFileName(path) != "StepMeasurement.cs")
                .SelectMany(path => File.ReadLines(path)
                    .Where(static line => OpeningRead().IsMatch(line))
                    .Select(line => $"{Path.GetRelativePath(root, path)}: {line.Trim()}")),
        ];

        Assert.True(found.Length == 0, $"These lines open a measured window themselves. Open it with StepMeasurement.Start():{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [GeneratedRegex(@"=\s*GC\.GetAllocatedBytesForCurrentThread\(\)\s*;")]
    private static partial Regex OpeningRead();
}
