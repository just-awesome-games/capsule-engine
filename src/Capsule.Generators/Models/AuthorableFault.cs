namespace Capsule.Generators;

/// <summary>An [Authorable] member no placement can set, or the later of two taking one key.</summary>
internal readonly record struct AuthorableFault(DeclaredAt At, string Member, string Key, string? Refusal, string? Clash);
