namespace ReleaseUpdaterGui
{
    // Value is the stable, persisted setting ("English"/"German", matching
    // AppLanguage) - DisplayName is what the combo box shows and is always
    // each language's own native name, regardless of which language is
    // currently active, so it doesn't need to change on a language switch.
    internal sealed class LanguageOption
    {
        public LanguageOption(string value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public string Value { get; }
        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
