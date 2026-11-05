using System.Runtime.CompilerServices;
using ReleaseUpdater.Core.Localization;

namespace ReleaseUpdater.Core.Tests;

// The app registers its texts once at start-up (Program.Main); the tests do the same, so that a
// text that is looked up is the real English one and not the bare key.
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void RegisterTexts()
    {
        AppStrings.Register();
    }
}
