using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Capsule.Tests.Packaging;

// The prose rules in AGENTS.md that a string match can hold, across every file the repo authors.
public sealed partial class ProseTests
{
    // Spelled as escapes so this file passes its own scan.
    private const string EmDash = "\u2014";
    private const string Chain = "\u002C so";

    private static readonly string[] Folders = ["src", "samples", "tests", "docs", "build", ".githooks"];

    private static readonly string[] Pages = ["README.md", "PACKAGE.md", "CONTRIBUTING.md", "SECURITY.md", "AGENTS.md"];

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".md", ".fx", ".csproj", ".props", ".targets", ".sh", "",
    };

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "TestResults", "results",
    };

    // Hidden and system entries are authored files too.
    private static readonly EnumerationOptions Everything = new() { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };

    [Fact]
    public void NoAuthoredLine_CarriesAnEmDashOrAConsequenceChain() =>
        Scan(Folders, Pages, (path, _) => DashesAndChains(path), $"Capsule's prose takes no em-dash and no \"X{Chain} Y\" consequence chain. Rewrite each of these lines as plain sentences");

    // Prose is a comment outside a code example, or a page's line outside code.
    [Fact]
    public void NoProse_JoinsClausesWithASemicolon() =>
        Scan(Folders, Pages, JoinedClauses, "Capsule's prose joins no two clauses with a semicolon. Split each of these lines into sentences");

    // A summary is one sentence, with a second only for a unit, an owner or a lifetime. A third is never
    // either, and belongs in remarks or returns.
    [Fact]
    public void NoSummary_RunsPastTwoSentences() =>
        Scan(["src", "samples", "tests"], [], (path, _) => LongSummaries(path), "These summaries run past two sentences, which AGENTS.md allows only for a unit, owner or lifetime fact. Keep one sentence and move the rest to remarks or returns");

    // Fails naming every line flagged across the folders' files and the root pages.
    private static void Scan(string[] folders, string[] pages, Func<string, string, IEnumerable<int>> flagged, string rule)
    {
        string root = RepositoryRoot();
        string[] broken =
        [
            .. folders.SelectMany(folder => Files(Path.Combine(root, folder)))
                .Concat(pages.Select(page => Path.Combine(root, page)))
                .SelectMany(path => flagged(path, root).Select(number => $"{Path.GetRelativePath(root, path)}:{number}")),
        ];

        Assert.True(broken.Length == 0, $"{rule}:{Environment.NewLine}{string.Join(Environment.NewLine, broken)}");
    }

    private static IEnumerable<int> DashesAndChains(string path)
    {
        int number = 0;
        bool comma = false;
        foreach (string line in File.ReadLines(path))
        {
            number++;

            // A chain broken across lines puts the "so" after the comma, or opens the next line with it.
            if (line.Contains(EmDash, StringComparison.Ordinal)
                || line.Contains(Chain + " ", StringComparison.Ordinal)
                || line.TrimEnd().EndsWith(Chain, StringComparison.Ordinal)
                || (comma && line.TrimStart().TrimStart('/', '*', '#').TrimStart().StartsWith("so ", StringComparison.Ordinal)))
            {
                yield return number;
            }

            comma = line.TrimEnd().EndsWith(',');
        }
    }

    private static IEnumerable<int> JoinedClauses(string path, string root)
    {
        bool markdown = path.EndsWith(".md", StringComparison.Ordinal);
        if (!markdown && (!path.EndsWith(".cs", StringComparison.Ordinal) || Vendored(path, root)))
        {
            yield break;
        }

        bool code = false;
        bool joined = false;
        int number = 0;
        foreach (string line in File.ReadLines(path))
        {
            number++;
            string? prose = WebUtility.HtmlDecode(markdown ? PageProse(line.Trim(), ref code) : CommentProse(line.Trim(), ref code));

            // A join broken across lines ends one line on the semicolon and opens the next in lower case.
            if (prose is not null && (JoinedClause().IsMatch(prose) || (joined && prose.Length > 0 && char.IsLower(prose[0]))))
            {
                yield return number;
            }

            joined = prose is not null && JoinedAtEnd().IsMatch(prose);
        }
    }

    // Each summary's opening line number.
    private static IEnumerable<int> LongSummaries(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.Ordinal))
        {
            yield break;
        }

        string[] lines = File.ReadAllLines(path);
        for (int index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Contains("/// <summary>", StringComparison.Ordinal))
            {
                continue;
            }

            int start = index;
            StringBuilder summary = new();
            for (; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                summary.Append(' ').Append(line.StartsWith("///", StringComparison.Ordinal) ? line[3..] : line);
                if (line.Contains("</summary>", StringComparison.Ordinal))
                {
                    break;
                }
            }

            if (SentenceEnd().Count(summary.Replace("<summary>", "").Replace("</summary>", "").ToString()) > 1)
            {
                yield return start + 1;
            }
        }
    }

    // A page's line with its inline code blanked, or null inside a fenced block.
    private static string? PageProse(string line, ref bool fenced)
    {
        if (line.StartsWith("```", StringComparison.Ordinal))
        {
            fenced = !fenced;

            return null;
        }

        return fenced ? null : InlineCode().Replace(line, "code");
    }

    // A comment's text, or null for code and for a line of an XML code example.
    private static string? CommentProse(string line, ref bool example)
    {
        if (!line.StartsWith("//", StringComparison.Ordinal))
        {
            example = false;

            return null;
        }

        string text = line.TrimStart('/').Trim();
        bool inside = example || text.Contains("<code>", StringComparison.Ordinal);
        example = inside && !text.Contains("</code>", StringComparison.Ordinal);

        return inside ? null : text;
    }

    // A word, number or closing mark, a semicolon, then a clause in lower case.
    [GeneratedRegex("[A-Za-z0-9)>`]; [a-z]")]
    private static partial Regex JoinedClause();

    [GeneratedRegex("[A-Za-z0-9)>`];$")]
    private static partial Regex JoinedAtEnd();

    [GeneratedRegex("`[^`]*`")]
    private static partial Regex InlineCode();

    // A sentence's end followed by the next sentence's start, a capital or a tag.
    [GeneratedRegex(@"[.!?]\s+(?=[A-Z<(])")]
    private static partial Regex SentenceEnd();

    // Code kept as its upstream wrote it sits under an .editorconfig declaring generated_code.
    internal static bool Vendored(string path, string root)
    {
        for (string? folder = Path.GetDirectoryName(path); folder is not null && folder.Length > root.Length; folder = Path.GetDirectoryName(folder))
        {
            string config = Path.Combine(folder, ".editorconfig");
            if (File.Exists(config) && File.ReadAllText(config).Contains("generated_code = true", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // A Skipped folder is pruned at any depth below the scanned one.
    internal static IEnumerable<string> Files(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", Everything).Where(path =>
                Extensions.Contains(Path.GetExtension(path))
                && !Path.GetRelativePath(folder, Path.GetDirectoryName(path)!).Split(Path.DirectorySeparatorChar).Any(Skipped.Contains))
            : [];

    internal static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Capsule.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No Capsule.slnx above {AppContext.BaseDirectory}. A repository scan runs from a build inside the repository.");
    }
}
