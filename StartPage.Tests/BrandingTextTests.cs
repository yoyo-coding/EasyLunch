using StartPage;
using Xunit;

namespace StartPage.Tests;

public class BrandingTextTests
{
    [Fact]
    public void VisibleBrandName_IsEasyLunch()
    {
        Assert.Equal("EasyLunch", BrandingText.BrandName);
    }

    [Fact]
    public void VisibleSubtitle_IsQuickLaunch()
    {
        Assert.Equal("快捷启动", BrandingText.Subtitle);
    }

    [Fact]
    public void WindowTitle_UsesBrandName()
    {
        Assert.Equal(BrandingText.BrandName, BrandingText.WindowTitle);
    }
}
