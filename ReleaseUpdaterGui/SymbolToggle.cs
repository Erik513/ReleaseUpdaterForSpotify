using ErikwnkWFUI;
using ErikwnkWFUI.Styles;

namespace ReleaseUpdaterGui
{
    // A library symbol button cannot change its preset once made, so a button
    // that switches between two looks (sign in / sign out, edit / save) is a
    // pair of preset buttons in one host, of which one is visible.
    internal sealed class SymbolToggle
    {
        private readonly Button first;
        private readonly Button second;

        public SymbolToggle(
            UIButtonPreset firstPreset,
            UIButtonPreset secondPreset,
            string firstTooltip,
            string secondTooltip,
            Size size,
            EventHandler click)
        {
            // The presets bring their own colors (blue); the app's buttons carry the accent.
            first = UIStyles.Buttons.CreateSymbol(
                firstPreset.WithColors(UIStyles.Colors.Sets.Primary), firstTooltip, size);
            second = UIStyles.Buttons.CreateSymbol(
                secondPreset.WithColors(UIStyles.Colors.Sets.Primary), secondTooltip, size);
            first.Dock = DockStyle.Fill;
            second.Dock = DockStyle.Fill;
            first.Margin = Padding.Empty;
            second.Margin = Padding.Empty;
            first.Click += click;
            second.Click += click;

            Host = UIStyles.Panels.CreateTransparent();
            Host.Size = size;
            Host.Controls.Add(first);
            Host.Controls.Add(second);
            ShowSecond(false);
        }

        public Control Host { get; }

        private Button Active
        {
            get { return second.Visible ? second : first; }
        }

        public bool Enabled
        {
            get { return Active.Enabled; }
            set
            {
                first.Enabled = value;
                second.Enabled = value;
            }
        }

        public void ShowSecond(bool useSecond)
        {
            second.Visible = useSecond;
            first.Visible = !useSecond;
        }

        public void SetTooltips(string firstTooltip, string secondTooltip)
        {
            UIStyles.Buttons.UpdateTooltip(first, firstTooltip);
            UIStyles.Buttons.UpdateTooltip(second, secondTooltip);
        }

        public void SetAccessibleNames(string firstName, string secondName)
        {
            first.AccessibleName = firstName;
            second.AccessibleName = secondName;
        }

        public void Focus()
        {
            Active.Focus();
        }
    }
}
