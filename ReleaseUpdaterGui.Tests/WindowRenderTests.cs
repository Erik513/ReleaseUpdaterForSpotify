using System.Drawing;
using ErikwnkWFUI;
using ReleaseUpdaterGui;

namespace ReleaseUpdaterGui.Tests;

/// <summary>
/// Pictures of the real windows without showing them (UITesting.Render): the windows are built, laid out and printed off the
/// screen, no message loop runs, so the Shown event (settings load, network) never starts. Set RELEASEUPDATER_RENDER_DIR to
/// keep the pictures for a look.
/// </summary>
public sealed class WindowRenderTests
{
    // Verifies that the main window can be built and printed, and that the picture is not empty.
    [Fact]
    public void MainForm_RendersAWindowWithContent()
    {
        RunOnStaThread(() =>
        {
            using MainForm form = new MainForm();
            using Bitmap picture = UITesting.Render(form);

            Assert.Equal(form.Width, picture.Width);
            Assert.Equal(form.Height, picture.Height);
            Assert.True(CountDistinctColors(picture) > 8, "the picture shows more than a flat surface");

            string? folder = Environment.GetEnvironmentVariable("RELEASEUPDATER_RENDER_DIR");

            if (!string.IsNullOrEmpty(folder))
            {
                UITesting.Render(form, Path.Combine(folder, "main.png"));
            }
        });
    }

    private static int CountDistinctColors(Bitmap picture)
    {
        HashSet<int> colors = new HashSet<int>();

        for (int y = 0; y < picture.Height; y += 3)
        {
            for (int x = 0; x < picture.Width; x += 3)
            {
                colors.Add(picture.GetPixel(x, y).ToArgb());
            }
        }

        return colors.Count;
    }

    // Windows Forms needs a single-threaded apartment.
    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw new Exception("The render failed: " + failure.Message, failure);
        }
    }
}
