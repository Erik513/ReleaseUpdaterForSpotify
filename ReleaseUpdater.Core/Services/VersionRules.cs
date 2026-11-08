using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static ReleaseUpdater.Core.Services.TextNormalization;

namespace ReleaseUpdater.Core.Services;

/// <summary>The rules for remixes, edits and other versions of a title: which version a tracked song means, and whether a candidate is that one.</summary>
internal static class VersionRules
{
    // ---- versions: remixes, edits, reworks ----

    // Words that say what kind of version it is ("Remix", "Edit", "Version" ...).
    internal static readonly HashSet<string> VersionKindWords = new(StringComparer.Ordinal)
    {
        "remix", "rework", "edit", "mix", "version", "bootleg", "flip", "vip", "refix", "mashup", "dub", "revision", "rebuild", "redo", "cover"
    };

    // The kinds that name somebody else's version of the song (a remix by X). "Radio Edit" is not one: its "radio" is no name.
    internal static readonly HashSet<string> RemixLikeKindWords = new(StringComparer.Ordinal)
    {
        "remix", "rework", "bootleg", "flip", "vip", "refix", "mashup", "dub", "revision", "rebuild", "redo", "edit"
    };

    // Words in a version that name no one: "Original Mix", "Extended Mix", "Radio Edit", "Album Version" are the song itself, in a
    // length or a form - they do not make it another release.
    internal static readonly HashSet<string> GenericVersionWords = new(StringComparer.Ordinal)
    {
        "original", "extended", "radio", "club", "album", "single", "instrumental", "short", "long", "full", "main", "the", "and", "with",
        "x", "vs", "pt", "part", "feat", "ft", "featuring", "remaster", "remastered", "live", "acoustic"
    };

    internal readonly record struct VersionTag(
        HashSet<string> Words,
        string[] Names,
        bool HasKind,
        bool HasRemixKind,
        bool IsUnknown);

    // What a title says about its version: the words in its brackets and behind a " - " (not the "feat." credit and not a note after "->"),
    // the names among them (the remixer; a year), and whether the remixer is "ID" - not known yet.
    internal static VersionTag ParseVersion(string title)
    {
        List<string> words = new();
        string withoutNote = title.Contains("->", StringComparison.Ordinal) ? title[..title.IndexOf("->", StringComparison.Ordinal)] : title;

        foreach (Match bracket in BracketedPart.Matches(withoutNote))
        {
            string inside = bracket.Value[1..^1];
            string[] insideWords = Words(inside);

            // ("(feat. Bob)" is a credit of the artist, no version.)
            if (insideWords.Length > 0 && insideWords[0] is "feat" or "ft" or "featuring")
            {
                continue;
            }

            words.AddRange(insideWords);
        }

        string rest = BracketedPart.Replace(withoutNote, " ");
        int dash = rest.IndexOf(" - ", StringComparison.Ordinal);

        if (dash >= 0)
        {
            string suffix = rest[(dash + 3)..];
            Match credit = FeatureCredit.Match(" " + suffix);
            words.AddRange(Words(credit.Success ? suffix[..Math.Max(0, credit.Index)] : suffix));
        }

        // A remaster is the song itself, whatever the year says.
        if (words.Contains("remaster") || words.Contains("remastered"))
        {
            words.Clear();
        }

        string[] names = words
            .Where(word => !VersionKindWords.Contains(word) && !GenericVersionWords.Contains(word) && word != "id")
            .Distinct()
            .ToArray();

        return new VersionTag(
            new HashSet<string>(words, StringComparer.Ordinal),
            names,
            words.Any(VersionKindWords.Contains),
            words.Any(RemixLikeKindWords.Contains),
            words.Contains("id"));
    }

    // Whether the tracked title and the title on Spotify are the same version of the song:
    //  - every name in the version of the entry (the remixer, a year) must be in the version of the track - so "Heal (David Mackay Remix)"
    //    is not "Heal", and not "Heal (Another Remix)";
    //  - an "ID" (a remixer that is not known yet) asks for a version of any kind on the track: "No Lie (ID Remix)" is not "No Lie";
    //  - a track that is somebody's remix must be one the entry names: the original that is tracked is not the remix of a stranger.
    // Names are compared with the same leniency as everything else (a typo does not matter).
    internal static bool VersionsFit(string trackedTitle, string candidateTitle)
    {
        VersionTag tracked = ParseVersion(trackedTitle);
        VersionTag candidate = ParseVersion(candidateTitle);

        foreach (string name in tracked.Names)
        {
            if (!IsAmong(name, candidate.Words))
            {
                return false;
            }
        }

        if (tracked.IsUnknown && !candidate.HasKind)
        {
            return false;
        }

        if (candidate.HasRemixKind && !tracked.IsUnknown)
        {
            foreach (string name in candidate.Names)
            {
                if (!IsAmong(name, tracked.Words))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static bool IsAmong(string word, HashSet<string> words) =>
        words.Contains(word) || (word.Length >= 5 && words.Any(other => other.Length >= 5 && SimilarityRatio(word, other) >= SimilarityThreshold));

    // A title that hardly belongs to another song: three words or more, or a long one. "Sammy In My Room" is one; "Revolution", "One Day" and
    // "Heal" are not (there are dozens of them), so for those the artist has to be close as well.
    internal static bool IsSpecificTitle(string title)
    {
        string core = StripExtras(title);
        string subject = core.Trim().Length > 0 ? core : title;

        return Words(subject).Length >= 3 || Normalize(subject).Length >= 14;
    }
}
