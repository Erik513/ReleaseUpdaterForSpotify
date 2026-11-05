using ErikwnkWFUI;
using ErikwnkWFUI.Styles;

namespace ReleaseUpdaterGui
{
    // One symbol button that switches between two looks (sign in / sign out,
    // edit / save) with UIStyles.Buttons.SetPreset; keeps the text of both
    // states so that a language switch can set them while the other is shown.
    internal sealed class SymbolToggle
    {
        private readonly Button button;
        private readonly UIButtonPreset firstPreset;
        private readonly UIButtonPreset secondPreset;
        private string firstTooltip;
        private string secondTooltip;
        private string firstName = string.Empty;
        private string secondName = string.Empty;
        private bool showingSecond;

        public SymbolToggle(
            UIButtonPreset first,
            UIButtonPreset second,
            string firstTooltip,
            string secondTooltip,
            Size size,
            EventHandler click)
        {
            // The presets bring their own colors (blue); the app's buttons carry the accent.
            firstPreset = first.WithColors(UIStyles.Colors.Sets.Primary);
            secondPreset = second.WithColors(UIStyles.Colors.Sets.Primary);
            this.firstTooltip = firstTooltip;
            this.secondTooltip = secondTooltip;

            button = UIStyles.Buttons.CreateSymbol(firstPreset, firstTooltip, size);
            button.Margin = Padding.Empty;
            button.Click += click;
        }

        public Control Host
        {
            get { return button; }
        }

        public bool Enabled
        {
            get { return button.Enabled; }
            set { button.Enabled = value; }
        }

        public void ShowSecond(bool useSecond)
        {
            showingSecond = useSecond;
            Apply();
        }

        public void SetTooltips(string first, string second)
        {
            firstTooltip = first;
            secondTooltip = second;
            Apply();
        }

        public void SetAccessibleNames(string first, string second)
        {
            firstName = first;
            secondName = second;
            Apply();
        }

        public void Focus()
        {
            button.Focus();
        }

        private void Apply()
        {
            UIStyles.Buttons.SetPreset(
                button,
                showingSecond ? secondPreset : firstPreset,
                showingSecond ? secondTooltip : firstTooltip);
            button.AccessibleName = showingSecond ? secondName : firstName;
        }
    }
}
