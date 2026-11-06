using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ReleaseUpdater.Core.Localization;
using ReleaseUpdater.Core.Services;

namespace ReleaseUpdaterGui
{
    /// <summary>
    /// The window for the link of one song: a box for the web address, and - when the song has a link already - a button that takes
    /// the link away at once (no clearing of the box and a confirmation). Only a web address is accepted; anything else stays in the box
    /// with a red frame and the reason.
    /// </summary>
    public sealed class LinkForm : AppForm
    {
        private static readonly Size ButtonSize = new(48, 34);

        private readonly TextBox txtLink;
        private readonly bool hadLink;

        /// <param name="prompt">The question above the box ("Link for ...: where did you find this song?").</param>
        /// <param name="currentLink">The link the song has, or null.</param>
        public LinkForm(string prompt, string? currentLink) : base(
            FormOptions.CreateDialog(
                title: AppLocalization.Get(AppStrings.UnreleasedFormTitle),
                titleTextAlign: ContentAlignment.MiddleCenter,
                backColor: UIStyles.Colors.BackgroundDarkElevated))
        {
            hadLink = !string.IsNullOrWhiteSpace(currentLink);

            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(480, 190);

            TableLayoutPanel layout = UIStyles.TableLayoutPanels.CreateStandard(
                columnCount: 1,
                rowCount: 4,
                colors: UIStyles.Colors.BackgroundLight);
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            Label lblPrompt = UIStyles.Labels.CreateStandard(
                prompt,
                scroll: ErikwnkWFUI.Controls.LabelScroll.BackAndForth(pauseOnHover: true));
            lblPrompt.Dock = DockStyle.Fill;
            lblPrompt.TextAlign = ContentAlignment.MiddleLeft;

            Label lblHint = UIStyles.Labels.CreateMuted(AppLocalization.Get(AppStrings.UnreleasedLinkHint));
            lblHint.Dock = DockStyle.Fill;
            lblHint.TextAlign = ContentAlignment.MiddleLeft;

            txtLink = UIStyles.TextBoxes.CreatePrimary(currentLink ?? string.Empty, "https://");
            txtLink.MaxLength = 2000;
            txtLink.Dock = DockStyle.Top;

            FlowLayoutPanel buttons = UIStyles.FlowLayoutPanels.CreateTransparent();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            // Symbols, not words: the tooltips say what they are. OK is the green check mark, Cancel the quiet gray one (the red one is
            // for taking the link away).
            Button btnCancel = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Cancel.WithColors(UIStyles.Buttons.Presets.Document.Colors),
                AppLocalization.Get(AppStrings.UnreleasedLinkCancel),
                ButtonSize);
            btnCancel.AccessibleName = AppLocalization.Get(AppStrings.UnreleasedLinkCancel);
            btnCancel.Margin = new Padding(8, 5, 0, 5);
            btnCancel.DialogResult = DialogResult.Cancel;

            Button btnOk = UIStyles.Buttons.CreateSymbol(
                UIStyles.Buttons.Presets.Check,
                AppLocalization.Get(AppStrings.UnreleasedLinkOk),
                ButtonSize);
            btnOk.AccessibleName = AppLocalization.Get(AppStrings.UnreleasedLinkOk);
            btnOk.Margin = new Padding(0, 5, 0, 5);
            btnOk.Click += (_, _) => Confirm();

            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);

            // The button that takes the link away stands at the left, away from OK; only for a song that has a link.
            Button? btnRemove = null;

            if (hadLink)
            {
                btnRemove = UIStyles.Buttons.CreateSymbol(
                    UIStyles.Buttons.Presets.Delete,
                    AppLocalization.Get(AppStrings.UnreleasedLinkRemove),
                    ButtonSize);
                btnRemove.AccessibleName = AppLocalization.Get(AppStrings.UnreleasedLinkRemove);
                btnRemove.Margin = new Padding(0, 5, 0, 5);
                btnRemove.Click += (_, _) => RemoveLink();
            }

            TableLayoutPanel bottom = UIStyles.TableLayoutPanels.CreateTransparent(columnCount: 2, rowCount: 1);
            bottom.Dock = DockStyle.Fill;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ButtonSize.Width + 12));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            if (btnRemove is not null)
            {
                bottom.Controls.Add(btnRemove, 0, 0);
            }

            bottom.Controls.Add(buttons, 1, 0);

            layout.Controls.Add(lblPrompt, 0, 0);
            layout.Controls.Add(lblHint, 0, 1);
            layout.Controls.Add(txtLink, 0, 2);
            layout.Controls.Add(bottom, 0, 3);
            ContentPanel.Controls.Add(layout);

            CancelButton = btnCancel;
            AcceptButton = btnOk;
            Shown += (_, _) => txtLink.SelectAll();
        }

        /// <summary>The address to keep (https:// in front where it was left out); null: the song has no link (the box was empty, or it was removed).</summary>
        public string? Address { get; private set; }

        /// <summary>True when the user took the link away with the button.</summary>
        public bool Removed { get; private set; }

        // OK: a web address (or nothing) is accepted; anything else stays in the box with the reason.
        private void Confirm()
        {
            string text = txtLink.Text.Trim();
            string? address = TrackedSongLinks.Normalize(text);

            if (text.Length > 0 && address is null)
            {
                if (txtLink is ErikwnkWFUI.Controls.TextField field)
                {
                    field.ErrorText = AppLocalization.Get(AppStrings.UnreleasedLinkInvalid);
                }

                txtLink.Focus();

                return;
            }

            Address = address;
            DialogResult = DialogResult.OK;
        }

        // The button: the link is gone, nothing else to do.
        private void RemoveLink()
        {
            Address = null;
            Removed = true;
            DialogResult = DialogResult.OK;
        }
    }
}
