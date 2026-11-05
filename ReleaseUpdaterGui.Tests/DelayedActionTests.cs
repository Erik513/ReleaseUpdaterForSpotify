namespace ReleaseUpdaterGui.Tests;

// The timing itself (a UI timer) needs a message loop and is not tested here; what matters - that the
// action runs once for any number of requests, that a flush runs it at once and that nothing runs
// that was cancelled - is.
public sealed class DelayedActionTests
{
    // Verifies that any number of requests is one run, when flushed.
    [Fact]
    public void ManyRequests_AreOneRun()
    {
        int runs = 0;
        using DelayedAction delayed = new(() => runs++, 10_000);

        for (int i = 0; i < 50; i++)
        {
            delayed.Schedule();
        }

        Assert.True(delayed.IsPending);
        Assert.Equal(0, runs);

        delayed.Flush();

        Assert.Equal(1, runs);
        Assert.False(delayed.IsPending);
    }

    // Verifies that flushing again (or with nothing asked for) runs nothing.
    [Fact]
    public void Flush_WithNothingPending_RunsNothing()
    {
        int runs = 0;
        using DelayedAction delayed = new(() => runs++, 10_000);

        delayed.Flush();
        delayed.Schedule();
        delayed.Flush();
        delayed.Flush();

        Assert.Equal(1, runs);
    }

    // Verifies that a cancelled request never runs, even when flushed afterwards.
    [Fact]
    public void Cancel_ForgetsThePendingRun()
    {
        int runs = 0;
        using DelayedAction delayed = new(() => runs++, 10_000);
        delayed.Schedule();

        delayed.Cancel();
        delayed.Flush();

        Assert.Equal(0, runs);
        Assert.False(delayed.IsPending);
    }

    // Verifies that after a run a new request runs again (it is not a one-shot).
    [Fact]
    public void AfterARun_ANewRequestRunsAgain()
    {
        int runs = 0;
        using DelayedAction delayed = new(() => runs++, 10_000);

        delayed.Schedule();
        delayed.Flush();
        delayed.Schedule();
        delayed.Flush();

        Assert.Equal(2, runs);
    }

    // Verifies that an action that asks for itself again while it runs (a save that changes a box)
    // is run again later, not lost and not run in a loop.
    [Fact]
    public void ARequestMadeWhileRunning_IsKeptForTheNextFlush()
    {
        int runs = 0;
        DelayedAction? delayed = null;
        delayed = new DelayedAction(
            () =>
            {
                runs++;

                if (runs == 1)
                {
                    delayed!.Schedule();
                }
            },
            10_000);

        delayed.Schedule();
        delayed.Flush();

        Assert.Equal(1, runs);
        Assert.True(delayed.IsPending);

        delayed.Flush();

        Assert.Equal(2, runs);
        delayed.Dispose();
    }

    // Verifies that an action that throws leaves the object usable (nothing stays "pending" forever).
    [Fact]
    public void AnActionThatThrows_DoesNotStayPending()
    {
        int runs = 0;
        using DelayedAction delayed = new(() =>
        {
            runs++;
            throw new IOException("disk full");
        }, 10_000);
        delayed.Schedule();

        Assert.Throws<IOException>(delayed.Flush);

        Assert.False(delayed.IsPending);
        delayed.Flush();
        Assert.Equal(1, runs);
    }

    // Verifies that a missing action is refused at once.
    [Fact]
    public void ANullAction_IsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new DelayedAction(null!, 100));
    }
}
