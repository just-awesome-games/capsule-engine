namespace Capsule.Build.Strings;

/// <summary>One validated string table file: its language and every key it defines, in document order.</summary>
/// <param name="Language">The BCP 47 tag its file name carries, spelled as .NET spells it.</param>
/// <param name="Entries">Every key, a group before the keys inside it.</param>
internal sealed record StringDocument(string Language, StringEntry[] Entries);

/// <summary>One key of a string table file: a text, a group of more keys, or a list of texts.</summary>
/// <param name="Path">The key's names from the file's root, joined by '.'. A list's texts are keyed by their position in it.</param>
/// <param name="Text">The text as authored, or null for a group or a list.</param>
/// <param name="Count">How many texts a list holds, else 0.</param>
internal sealed record StringEntry(string Path, string? Text, int Count = 0);
