using ErikwnkWFUI.Forms;

namespace ReleaseUpdaterGui
{
    /// <summary>The three plain message boxes the windows use (they each had their own copy).</summary>
    internal static class Dialogs
    {
        public static void Info(Form owner, string message, string title) =>
            Show(owner, message, title, MessageBoxIcon.Info);

        public static void Warning(Form owner, string message, string title) =>
            Show(owner, message, title, MessageBoxIcon.Warning);

        public static void Error(Form owner, string message, string title) =>
            Show(owner, message, title, MessageBoxIcon.Error);

        /// <summary>A question with Yes, No and Cancel; No is the button that Enter answers (the one that loses nothing).</summary>
        public static DialogResult AskYesNoCancel(Form owner, string message, string title) =>
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                owner,
                FormSize.Medium,
                defaultButton: MessageBoxDefault.Button2);

        private static void Show(Form owner, string message, string title, MessageBoxIcon icon)
        {
            MessageBox.Show(
                message,
                title,
                MessageBoxButtons.OK,
                icon,
                owner,
                FormSize.Small);
        }
    }
}
