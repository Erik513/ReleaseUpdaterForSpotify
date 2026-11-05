using System.Reflection;
using System.Text.RegularExpressions;
using ReleaseUpdater.Core.Localization;

namespace ReleaseUpdater.Core.Tests;

// Every text the app shows exists in English and German, and both use the same {0}, {1} ... .
// (A missing German text silently shows the English one; a missing or extra placeholder throws
// a FormatException in front of the user.) The tables are read by reflection, so the tests do
// not depend on the language that is currently set.
public sealed class AppStringsTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:[:,][^}]*)?\}", RegexOptions.Compiled);

    // Verifies that every key declared in AppStrings has an English and a German text.
    [Fact]
    public void EveryKey_HasAnEnglishAndAGermanText()
    {
        Dictionary<string, string> english = Table("English");
        Dictionary<string, string> german = Table("German");

        string[] missingEnglish = Keys().Where(key => !english.ContainsKey(key)).ToArray();
        string[] missingGerman = Keys().Where(key => !german.ContainsKey(key)).ToArray();

        Assert.True(missingEnglish.Length == 0, "No English text for: " + string.Join(", ", missingEnglish));
        Assert.True(missingGerman.Length == 0, "No German text for: " + string.Join(", ", missingGerman));
    }

    // Verifies that no text is registered for a key that no longer exists (a leftover after a rename).
    [Fact]
    public void NoText_BelongsToAnUnknownKey()
    {
        HashSet<string> keys = Keys().ToHashSet();

        string[] stray = Table("English").Keys.Concat(Table("German").Keys)
            .Where(key => !keys.Contains(key))
            .Distinct()
            .ToArray();

        Assert.True(stray.Length == 0, "Texts without a key: " + string.Join(", ", stray));
    }

    // Verifies that both languages cover exactly the same keys.
    [Fact]
    public void BothLanguages_HaveTheSameKeys()
    {
        Assert.Equal(
            Table("English").Keys.OrderBy(key => key, StringComparer.Ordinal),
            Table("German").Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    // Verifies that no text is empty or blank.
    [Fact]
    public void NoText_IsEmpty()
    {
        string[] empty = Table("English").Concat(Table("German"))
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();

        Assert.True(empty.Length == 0, "Empty texts: " + string.Join(", ", empty));
    }

    // Verifies that the English and the German text of a key use the same placeholders.
    [Fact]
    public void EnglishAndGerman_UseTheSamePlaceholders()
    {
        Dictionary<string, string> english = Table("English");
        Dictionary<string, string> german = Table("German");

        string[] different = english.Keys
            .Where(key => german.ContainsKey(key))
            .Where(key => !PlaceholdersOf(english[key]).SequenceEqual(PlaceholdersOf(german[key])))
            .ToArray();

        Assert.True(different.Length == 0, "Different placeholders in: " + string.Join(", ", different));
    }

    // Verifies that every text formats without an exception when given enough arguments, so a
    // stray "{" or a placeholder number that is too large is found here and not by the user.
    [Fact]
    public void EveryText_Formats_WithItsPlaceholders()
    {
        foreach ((string language, Dictionary<string, string> table) in new[]
        {
            ("English", Table("English")),
            ("German", Table("German"))
        })
        {
            foreach ((string key, string text) in table)
            {
                int count = PlaceholdersOf(text).DefaultIfEmpty(-1).Max() + 1;
                object[] arguments = Enumerable.Range(0, count).Select(i => (object)("arg" + i)).ToArray();

                string formatted = null!;
                Exception? failure = Record.Exception(() => formatted = string.Format(text, arguments));

                Assert.True(failure is null, $"{language} text for {key} does not format: {failure?.Message}");
                Assert.NotNull(formatted);
            }
        }
    }

    // Verifies that a key that is named "...Format" really has a placeholder, and one that is not does not.
    [Fact]
    public void FormatKeys_HavePlaceholders_AndOthersDoNot()
    {
        Dictionary<string, string> english = Table("English");
        Dictionary<string, string> constants = ConstantsByName();

        string[] wrong = constants
            .Where(pair => english.ContainsKey(pair.Value))
            .Where(pair =>
            {
                bool namedFormat = pair.Key.Contains("Format", StringComparison.Ordinal);
                bool hasPlaceholder = PlaceholdersOf(english[pair.Value]).Any();

                return namedFormat != hasPlaceholder;
            })
            .Select(pair => pair.Key)
            .ToArray();

        Assert.True(wrong.Length == 0, "Name and placeholders disagree for: " + string.Join(", ", wrong));
    }

    // Verifies that no two constants share one key value (two names for the same text).
    [Fact]
    public void Keys_AreUnique()
    {
        string[] duplicates = Keys().GroupBy(key => key).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();

        Assert.True(duplicates.Length == 0, "Duplicate keys: " + string.Join(", ", duplicates));
    }

    private static IEnumerable<int> PlaceholdersOf(string text) =>
        Placeholder.Matches(text).Select(match => int.Parse(match.Groups[1].Value)).Distinct().OrderBy(n => n);

    private static IEnumerable<string> Keys() => ConstantsByName().Values;

    private static Dictionary<string, string> ConstantsByName() =>
        typeof(AppStrings)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

    private static Dictionary<string, string> Table(string name)
    {
        FieldInfo? field = typeof(AppStrings).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);

        return (Dictionary<string, string>)field!.GetValue(null)!;
    }
}
