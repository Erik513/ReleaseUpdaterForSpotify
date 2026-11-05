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
