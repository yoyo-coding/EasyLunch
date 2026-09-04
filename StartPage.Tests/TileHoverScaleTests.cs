using StartPage;
using Xunit;

namespace StartPage.Tests;

public class TileHoverScaleTests
{
    [Fact]
    public void HoveringSelectedArea_UsesExpandedScale()
    {
        Assert.Equal(1.06, TileHoverScale.GetScale(TilePointerState.Entered));
    }

    [Fact]
    public void LeavingSelectedArea_RestoresNormalScale()
    {
        Assert.Equal(1.0, TileHoverScale.GetScale(TilePointerState.Exited));
    }

    [Fact]
    public void PressingSelectedArea_UsesPressedScale()
    {
        Assert.Equal(0.96, TileHoverScale.GetScale(TilePointerState.Pressed));
    }
}
