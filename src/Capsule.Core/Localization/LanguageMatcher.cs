namespace Capsule.Localization;

// Picks the shipped language closest to a list of preferred BCP 47 tags, allocating nothing.
//
// Each preferred tag is tried in order before the next: an exact match, ignoring case and reading '_' as
// '-', then the tag with trailing subtags dropped, as de-AT to de, then the same primary language in any
// other region, as pt-PT to pt-BR. Chinese matches only within its script: zh-TW, zh-HK, zh-MO and
// zh-Hant are Traditional, and zh, zh-CN, zh-SG and zh-Hans Simplified.
internal static class LanguageMatcher
{
    private enum Script
    {
        None,
        Simplified,
        Traditional,
    }

    // The index in shipped that best serves preferred, or -1 when none does.
    internal static int Match(ReadOnlySpan<string> shipped, ReadOnlySpan<string> preferred)
    {
        foreach (string? wanted in preferred)
        {
            if (string.IsNullOrWhiteSpace(wanted))
            {
                continue;
            }

            ReadOnlySpan<char> tag = wanted.AsSpan().Trim();
            for (int i = 0; i < shipped.Length; i++)
            {
                if (SameTag(shipped[i], tag))
                {
                    return i;
                }
            }

            Script script = ScriptOf(tag);
            for (ReadOnlySpan<char> shorter = tag; shorter.LastIndexOfAny('-', '_') is > 0 and int cut;)
            {
                shorter = shorter[..cut];
                for (int i = 0; i < shipped.Length; i++)
                {
                    if (SameTag(shipped[i], shorter) && ScriptOf(shipped[i]) == script)
                    {
                        return i;
                    }
                }
            }

            ReadOnlySpan<char> primary = Primary(tag);
            for (int i = 0; i < shipped.Length; i++)
            {
                if (SameTag(Primary(shipped[i]), primary) && ScriptOf(shipped[i]) == script)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static ReadOnlySpan<char> Primary(ReadOnlySpan<char> tag) =>
        tag.IndexOfAny('-', '_') is >= 0 and int end ? tag[..end] : tag;

    // Equal ignoring ASCII case, with '_' read as '-'.
    private static bool SameTag(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        for (int i = 0; i < first.Length; i++)
        {
            char a = first[i] == '_' ? '-' : char.ToLowerInvariant(first[i]);
            char b = second[i] == '_' ? '-' : char.ToLowerInvariant(second[i]);
            if (a != b)
            {
                return false;
            }
        }

        return true;
    }

    // A Chinese tag's script, written or implied by its region, and None for any other language.
    private static Script ScriptOf(ReadOnlySpan<char> tag)
    {
        if (!SameTag(Primary(tag), "zh"))
        {
            return Script.None;
        }

        bool traditionalRegion = false;
        ReadOnlySpan<char> rest = tag[Primary(tag).Length..];
        while (rest.Length > 1)
        {
            rest = rest[1..];
            ReadOnlySpan<char> subtag = rest.IndexOfAny('-', '_') is >= 0 and int end ? rest[..end] : rest;
            if (SameTag(subtag, "Hant"))
            {
                return Script.Traditional;
            }

            if (SameTag(subtag, "Hans"))
            {
                return Script.Simplified;
            }

            traditionalRegion |= SameTag(subtag, "TW") || SameTag(subtag, "HK") || SameTag(subtag, "MO");
            rest = rest[subtag.Length..];
        }

        return traditionalRegion ? Script.Traditional : Script.Simplified;
    }
}
