using System.Collections.Immutable;
using Capsule.Generators;
using Capsule.Scenes;
using Microsoft.CodeAnalysis;
using static Capsule.Tests.Analyzers.GameBoundaryFixtures;

namespace Capsule.Tests.Analyzers;

public sealed class GameBoundaryRandomnessTests
{
    // CAP105 closes the ambient APIs, so the seam it leaves open has to stay open.
    [Fact]
    public async Task Logic_accepts_the_seeded_random_source_reached_through_the_run()
    {
        const string source = """
            using Capsule.Scenes;

            public sealed class Blinker : Entity
            {
                public Blinker() : base(default) { }

                public int NextBlink() => Random.Range(30, 90);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(
            source,
            logic: true,
            extraReferences:
            [
                MetadataReference.CreateFromFile(typeof(StepContext).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Entity).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Numerics.Vector2).Assembly.Location),
            ]);

        Assert.Empty(diagnostics);
    }

    // A method group is a use the invocation hook never sees, and the delegate outlives it.
    [Fact]
    public async Task Logic_rejects_a_system_random_member_taken_as_a_method_group()
    {
        const string source = """
            using System;

            public static class Logic
            {
                public static Func<int> Escape(Random random) => random.Next;
                public static Func<int> Wrapped(Random random) => new Func<int>(random.Next);
                public static Func<int> Shared() => Random.Shared.Next;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        // Three method groups, and the Random.Shared read one of them is taken from.
        Assert.Equal(4, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientRandomId));
    }

    [Fact]
    public async Task Logic_rejects_blocked_operations_taken_as_method_groups()
    {
        const string source = """
            using System;
            using System.Diagnostics;
            using System.IO;
            using System.Threading;

            public static class Logic
            {
                public static Func<string, bool> Io() => File.Exists;
                public static Action<object> Concurrency() => Monitor.Enter;
                public static Func<long> Time() => Stopwatch.GetTimestamp;
                public static Func<Guid> Random() => Guid.NewGuid;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ExternalIoId);
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.ConcurrencyId);
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientTimeId);
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientRandomId);
    }

    // System.Random's seeded sequence is not stable across runtime versions.
    [Fact]
    public async Task Logic_rejects_a_seeded_system_random_it_constructs_holds_or_draws_from()
    {
        const string source = """
            using System;

            public sealed class Logic
            {
                private readonly Random random = new(42);
                public int Next() => random.Next();
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await Analyze(source, logic: true);

        Assert.Equal(3, diagnostics.Count(diagnostic => diagnostic.Id == GameBoundaryAnalyzer.AmbientRandomId));
    }
}
