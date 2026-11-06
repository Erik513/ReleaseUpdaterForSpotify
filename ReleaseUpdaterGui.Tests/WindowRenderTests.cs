using System.Drawing;
using ErikwnkWFUI;
using ReleaseUpdater.Core.Services;
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

            // The strip with the version at the bottom of the window.
            ErikwnkWFUI.Controls.VersionPanel strip = Assert.Single(FindAll<ErikwnkWFUI.Controls.VersionPanel>(form));
            Assert.False(string.IsNullOrWhiteSpace(strip.VersionText));
            Assert.False(strip.UpdateAvailable);

            string? folder = Environment.GetEnvironmentVariable("RELEASEUPDATER_RENDER_DIR");

            if (!string.IsNullOrEmpty(folder))
            {
                UITesting.Render(form, Path.Combine(folder, "main.png"));
            }
        });
    }

    // Verifies that the unreleased-songs window can be built and printed, and that the picture is not empty.
    [Fact]
    public void UnreleasedSongsForm_RendersAWindowWithContent()
    {
        RunOnStaThread(() =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "SpotifyReleaseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                FakeUnreleasedReportWriter reportWriter = new();
                using UnreleasedSongsForm form = new(
                    new ReleaseUpdater.Data.Storage.TrackedSongsStore(new ReleaseUpdater.Data.Storage.AppDataPaths(directory)),
                    reportWriter,
                    new ReleaseUpdater.Core.Services.UnreleasedSongChecker(new FakeGateway(), reportWriter),
                    new FakeSettingsStore());
                using Bitmap picture = UITesting.Render(form);

                Assert.Equal(form.Width, picture.Width);
                Assert.True(CountDistinctColors(picture) > 8, "the picture shows more than a flat surface");

                string? folder = Environment.GetEnvironmentVariable("RELEASEUPDATER_RENDER_DIR");

                if (!string.IsNullOrEmpty(folder))
                {
                    UITesting.Render(form, Path.Combine(folder, "unreleased.png"));
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    // Verifies that the dialog for the columns of an imported table can be built and printed, and that its choice starts with the proposal.
    [Fact]
    public void ImportColumnsForm_RendersAWindow_AndStartsWithTheProposal()
    {
        RunOnStaThread(() =>
        {
            ImportTable table = new(new IReadOnlyList<string>[]
            {
                new[] { "Nr", "Titel", "Jahr", "Interpret" },
                new[] { "1", "Some Song", "2020", "Some Artist" },
                new[] { "2", "Another Song With A Rather Long Title", "2021", "Another Artist" }
            });

            using ImportColumnsForm form = new(table);
            using Bitmap picture = UITesting.Render(form);

            Assert.Equal(new ImportColumnChoice(1, 3, true), form.Choice);
            Assert.True(CountDistinctColors(picture) > 8, "the picture shows more than a flat surface");

            string? folder = Environment.GetEnvironmentVariable("RELEASEUPDATER_RENDER_DIR");

            if (!string.IsNullOrEmpty(folder))
            {
                UITesting.Render(form, Path.Combine(folder, "columns.png"));
            }
        });
    }

    // Verifies the behavior of the dialog for the columns: a meaning belongs to one column (choosing it for another takes it from the
    // first), "Import" needs a title and an artist, and the link is optional.
    [Fact]
    public void ImportColumnsForm_ChoosingAMeaningTakesItFromTheOtherColumn()
    {
        RunOnStaThread(() =>
        {
            ImportTable table = new(new IReadOnlyList<string>[]
            {
                new[] { "Nr", "Titel", "Interpret", "Quelle" },
                new[] { "1", "Song", "Artist", "https://example.com" }
            });

            using ImportColumnsForm form = new(table);
            ComboBox[] choices = (ComboBox[])typeof(ImportColumnsForm)
                .GetField("choices", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(form)!;
            Button ok = (Button)typeof(ImportColumnsForm)
                .GetField("btnOk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(form)!;

            // The proposal: title 1, artist 2, link 3.
            Assert.Equal(new ImportColumnChoice(1, 2, true, 3), form.Choice);
            Assert.True(ok.Enabled);

            // The artist is wanted in the column of the title: the title column is skipped now, and "Import" waits for a title.
            choices[1].SelectedIndex = 2;
            Assert.Equal(0, choices[2].SelectedIndex);
            Assert.False(ok.Enabled);

            // A title in the first column: ready again.
            choices[0].SelectedIndex = 1;
            Assert.Equal(new ImportColumnChoice(0, 1, true, 3), form.Choice);
            Assert.True(ok.Enabled);

            // No link is fine.
            choices[3].SelectedIndex = 0;
            Assert.Null(form.Choice.LinkColumn);
            Assert.True(ok.Enabled);
        });
    }

    // Verifies the window for the link of a song: with a link it has a button that takes it away at once; an invalid text is refused with
    // the reason and the window stays open; a web address is accepted in the form that is kept; without a link there is no such button.
    [Fact]
    public void LinkForm_RemovesAtOnce_RefusesWhatIsNoAddress_AndKeepsTheAddress()
    {
        RunOnStaThread(() =>
        {
            const System.Reflection.BindingFlags nonPublic = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            // A song that has a link: the button is there, one click removes it.
            using (LinkForm withLink = new("Link for \"Song\": where did you find this song?", "https://example.com/a"))
            {
                Assert.Contains(FindAll<Button>(withLink), button => button.AccessibleName == "Remove link");

                typeof(LinkForm).GetMethod("RemoveLink", nonPublic)!.Invoke(withLink, null);

                Assert.True(withLink.Removed);
                Assert.Null(withLink.Address);
                Assert.Equal(DialogResult.OK, withLink.DialogResult);
            }

            // A text that is no web address: refused, the window stays open, the frame says why.
            using (LinkForm editing = new("Link", "https://example.com/a"))
            {
                TextBox box = Assert.Single(FindAll<TextBox>(editing));
                box.Text = "see the video";

                typeof(LinkForm).GetMethod("Confirm", nonPublic)!.Invoke(editing, null);

                Assert.NotEqual(DialogResult.OK, editing.DialogResult);
                Assert.False(string.IsNullOrEmpty(((ErikwnkWFUI.Controls.TextField)box).ErrorText));

                // A web address: accepted, with https:// where it was left out.
                box.Text = " www.example.com/set ";
                typeof(LinkForm).GetMethod("Confirm", nonPublic)!.Invoke(editing, null);

                Assert.Equal(DialogResult.OK, editing.DialogResult);
                Assert.Equal("https://www.example.com/set", editing.Address);
                Assert.False(editing.Removed);
            }

            // A song without a link: no button to remove one; an empty box with OK is "no link".
            using (LinkForm without = new("Link", null))
            {
                Assert.DoesNotContain(FindAll<Button>(without), button => button.AccessibleName == "Remove link");

                typeof(LinkForm).GetMethod("Confirm", nonPublic)!.Invoke(without, null);

                Assert.Equal(DialogResult.OK, without.DialogResult);
                Assert.Null(without.Address);
            }

            // (And it can be printed.)
            using LinkForm picture = new("Link for \"One (ID Version)\": where did you find this song?", "https://www.youtube.com/watch?v=abc");
            using Bitmap rendered = UITesting.Render(picture);
            string? folder = Environment.GetEnvironmentVariable("RELEASEUPDATER_RENDER_DIR");

            if (!string.IsNullOrEmpty(folder))
            {
                UITesting.Render(picture, Path.Combine(folder, "link.png"));
            }

            Assert.True(CountDistinctColors(rendered) > 8);
        });
    }

    // Verifies that the empty row at the end of the list has no link: not when it is waiting, and not while it is being filled (the list
    // has an item for it then, but it is still the "new" row of the grid); a row that is a song has one.
    [Fact]
    public void UnreleasedSongsForm_TheNewRowHasNoLink()
    {
        RunOnStaThread(() =>
        {
            const System.Reflection.BindingFlags nonPublic = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            string directory = Path.Combine(Path.GetTempPath(), "SpotifyReleaseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                FakeUnreleasedReportWriter reportWriter = new();
                using UnreleasedSongsForm form = new(
                    new ReleaseUpdater.Data.Storage.TrackedSongsStore(new ReleaseUpdater.Data.Storage.AppDataPaths(directory)),
                    reportWriter,
                    new ReleaseUpdater.Core.Services.UnreleasedSongChecker(new FakeGateway(), reportWriter),
                    new FakeSettingsStore());
                // (A print makes the window and its grid, so that the grid has its rows; it is not shown for that.)
                using Bitmap warmUp = UITesting.Render(form);

                typeof(UnreleasedSongsForm).GetMethod("ShowSongs", nonPublic)!.Invoke(
                    form,
                    new object[] { new[] { new ReleaseUpdater.Core.Models.TrackedSong("One", "A"), new ReleaseUpdater.Core.Models.TrackedSong("Two", "B") } });

                DataGridView grid = (DataGridView)typeof(UnreleasedSongsForm).GetField("dataGridView", nonPublic)!.GetValue(form)!;
                System.Reflection.MethodInfo isSongRow = typeof(UnreleasedSongsForm).GetMethod("IsSongRow", nonPublic)!;
                bool IsSong(int row) => (bool)isSongRow.Invoke(form, new object[] { row })!;

                Assert.True(IsSong(0), "row 0, grid rows " + grid.Rows.Count);
                Assert.True(IsSong(1), "row 1");
                Assert.False(IsSong(2));                       // the empty row at the end
                Assert.False(IsSong(-1));                      // the header
                Assert.False(IsSong(3));                       // nothing

                // The user clicks into the empty row: the list gets an item for it, the row is still the new one.
                grid.CurrentCell = grid.Rows[grid.Rows.Count - 1].Cells[0];

                Assert.True(grid.Rows[grid.Rows.Count - 1].IsNewRow, "last row is new");
                Assert.False(IsSong(grid.Rows.Count - 1));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    // Verifies that copying cells copies the address of a link cell - not the symbol that is drawn in it - and nothing for a row without
    // a link; and that the cells of a row come out as title, artist, link (the delete column is left out by the grid).
    [Fact]
    public void UnreleasedSongsForm_CopyingALinkCell_CopiesTheAddress()
    {
        RunOnStaThread(() =>
        {
            const System.Reflection.BindingFlags nonPublic = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            string directory = Path.Combine(Path.GetTempPath(), "SpotifyReleaseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                FakeUnreleasedReportWriter reportWriter = new();
                using UnreleasedSongsForm form = new(
                    new ReleaseUpdater.Data.Storage.TrackedSongsStore(new ReleaseUpdater.Data.Storage.AppDataPaths(directory)),
                    reportWriter,
                    new ReleaseUpdater.Core.Services.UnreleasedSongChecker(new FakeGateway(), reportWriter),
                    new FakeSettingsStore());
                using Bitmap warmUp = UITesting.Render(form);

                typeof(UnreleasedSongsForm).GetMethod("ShowSongs", nonPublic)!.Invoke(
                    form,
                    new object[]
                    {
                        new[]
                        {
                            new ReleaseUpdater.Core.Models.TrackedSong("One", "A", "https://example.com/a"),
                            new ReleaseUpdater.Core.Models.TrackedSong("Two", "B")
                        }
                    });

                DataGridView grid = (DataGridView)typeof(UnreleasedSongsForm).GetField("dataGridView", nonPublic)!.GetValue(form)!;

                string Copy(params (int Row, string Column)[] cells)
                {
                    grid.ClearSelection();

                    foreach ((int row, string column) in cells)
                    {
                        grid.Rows[row].Cells[column].Selected = true;
                    }

                    return grid.GetClipboardContent()!.GetText().TrimEnd((char)13, (char)10);
                }

                Assert.Equal("https://example.com/a", Copy((0, "colLink")));
                Assert.Equal(string.Empty, Copy((1, "colLink")));
                Assert.Equal("One" + (char)9 + "A" + (char)9 + "https://example.com/a", Copy((0, "colTitle"), (0, "colArtist"), (0, "colLink")));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static IEnumerable<T> FindAll<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T inner in FindAll<T>(child))
            {
                yield return inner;
            }
        }
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
