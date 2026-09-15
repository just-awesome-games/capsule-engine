using Capsule.Assets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Capsule.Generators;

internal enum ParsedFault
{
    None,
    UnsafeName,
    Unreadable,
}

/// <summary>Reads one authored document, or states why it could not be read.</summary>
/// <param name="line">The zero-based line the defect is on.</param>
internal delegate T? ParseText<T>(string text, out string? error, out int line)
    where T : class;

// One authored text asset as the generator read it. What was parsed is compared by reference: a
// re-parse regenerates the registry whether or not the bytes changed, which is conservative, never
// stale.
internal readonly struct ParsedAsset<T>(
    string key,
    string display,
    ParsedFault fault,
    string? message,
    T? parsed,
    Location location)
    : IEquatable<ParsedAsset<T>>
    where T : class
{
    /// <summary>The source's key under its domain root, or its authored path when that is no key.</summary>
    internal string Key { get; } = key;

    /// <summary>What a diagnostic names the source by: its path under the source tree.</summary>
    internal string Display { get; } = display;

    internal ParsedFault Fault { get; } = fault;

    /// <summary>Why the source could not be read, when it could not.</summary>
    internal string? Message { get; } = message;

    /// <summary>What the source parsed to, null where it did not parse.</summary>
    internal T? Parsed { get; } = parsed;

    /// <summary>The source itself, at the line the defect is on: what a build error navigates to.</summary>
    internal Location Location { get; } = location;

    public bool Equals(ParsedAsset<T> other) =>
        Fault == other.Fault
        && string.Equals(Key, other.Key, StringComparison.Ordinal)
        && string.Equals(Display, other.Display, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal)
        && ReferenceEquals(Parsed, other.Parsed)
        && Location.Equals(other.Location);

    public override bool Equals(object? obj) => obj is ParsedAsset<T> other && Equals(other);

    public override int GetHashCode() =>
        (Key.GetHashCode() * 31) ^ (Display.GetHashCode() * 17) ^ (int)Fault;
}

internal static class ParsedAsset
{
    /// <summary>Reads one additional file of a text domain into the model its registry is built from.</summary>
    /// <param name="authored">The path the asset hook authored the file at, extension stripped.</param>
    /// <param name="domain">The domain root the file is authored under.</param>
    /// <param name="extension">The extension the domain's sources carry, both halves of it.</param>
    /// <param name="unreadable">Why a file the compiler cannot read as text is one it cannot read.</param>
    internal static ParsedAsset<T> Describe<T>(
        AdditionalText text,
        string authored,
        string domain,
        string extension,
        string unreadable,
        ParseText<T> parse,
        CancellationToken cancellation)
        where T : class
    {
        string display = domain + "/" + authored + extension;
        SourceText? content = text.GetText(cancellation);

        if (TypeNaming.NormalizeKey(authored, out _) is not { } key)
        {
            return new ParsedAsset<T>(authored, display, ParsedFault.UnsafeName, null, null, At(text.Path, content, 0));
        }

        if (!AssetPaths.IsKey(key))
        {
            return new ParsedAsset<T>(
                key,
                display,
                ParsedFault.Unreadable,
                $"keys as \"{key}\"; a segment of a key is no reserved Windows device name (nul, con, ...).",
                null,
                At(text.Path, content, 0));
        }

        if (content is null)
        {
            return new ParsedAsset<T>(key, display, ParsedFault.Unreadable, unreadable, null, At(text.Path, null, 0));
        }

        T? parsed = parse(content.ToString(), out string? error, out int line);

        return parsed is null
            ? new ParsedAsset<T>(key, display, ParsedFault.Unreadable, error, null, At(text.Path, content, line))
            : new ParsedAsset<T>(key, display, ParsedFault.None, null, parsed, At(text.Path, content, 0));
    }

    /// <summary>The file itself as a location a build error navigates to, at its head.</summary>
    internal static Location At(string path) => At(path, null, 0);

    // The compiler holds no syntax tree for an additional file, so the span is spelled out against
    // the file itself.
    internal static Location At(string path, SourceText? content, int line)
    {
        if (content is null || line >= content.Lines.Count)
        {
            return Location.Create(path, new TextSpan(0, 0), new LinePositionSpan(default, default));
        }

        TextLine text = content.Lines[line];

        return Location.Create(
            path,
            text.Span,
            new LinePositionSpan(new LinePosition(line, 0), new LinePosition(line, text.Span.Length)));
    }
}
