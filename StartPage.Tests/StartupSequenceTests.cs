using StartPage;
using Xunit;

namespace StartPage.Tests;

public class StartupSequenceTests
{
    [Fact]
    public void OnConstruct_DoesNotCallConfigureWindowOrApplyBackdrop()
    {
        var configureCalled = false;
        var backdropCalled = false;
        var loadedCalled = false;

        var seq = new StartupSequence(
            () => configureCalled = true,
            () => backdropCalled = true,
            () => loadedCalled = true);

        seq.OnConstruct();

        Assert.False(configureCalled, "ConfigureWindow must not be called during construction");
        Assert.False(backdropCalled, "ApplyBackdrop must not be called during construction");
        Assert.False(loadedCalled, "OnLoaded callback must not be called during construction");
        Assert.False(seq.IsLoaded);
    }

    [Fact]
    public void OnLoaded_CallsConfigureWindowThenApplyBackdropThenOnLoaded()
    {
        var callOrder = new List<string>();

        var seq = new StartupSequence(
            () => callOrder.Add("ConfigureWindow"),
            () => callOrder.Add("ApplyBackdrop"),
            () => callOrder.Add("OnLoaded"));

        seq.OnLoaded();

        Assert.Equal(new[] { "ConfigureWindow", "ApplyBackdrop", "OnLoaded" }, callOrder);
        Assert.True(seq.IsLoaded);
    }

    [Fact]
    public void OnLoaded_OnlyExecutesOnce_EvenIfCalledMultipleTimes()
    {
        var configureCount = 0;
        var backdropCount = 0;
        var loadedCount = 0;

        var seq = new StartupSequence(
            () => configureCount++,
            () => backdropCount++,
            () => loadedCount++);

        seq.OnLoaded();
        seq.OnLoaded();
        seq.OnLoaded();

        Assert.Equal(1, configureCount);
        Assert.Equal(1, backdropCount);
        Assert.Equal(1, loadedCount);
    }

    [Fact]
    public void OnConstruct_ResetsToUnloaded_WhenCalledAfterOnLoaded()
    {
        var seq = new StartupSequence(
            () => { },
            () => { },
            () => { });

        seq.OnLoaded();
        Assert.True(seq.IsLoaded);

        seq.OnConstruct();
        Assert.False(seq.IsLoaded);
    }
}
