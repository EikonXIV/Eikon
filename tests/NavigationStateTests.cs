using Eikon.Navigation;
using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

public class NavigationStateTests
{
    [Fact]
    public void ScreenRouter_starts_on_its_initial_screen_and_navigates()
    {
        var router = new ScreenRouter(Screen.Grid);
        Assert.Equal(Screen.Grid, router.Current);
        router.Navigate(Screen.Settings);
        Assert.Equal(Screen.Settings, router.Current);
    }

    [Fact]
    public void Selection_has_sensible_defaults()
    {
        var s = new Selection();
        Assert.Null(s.ProfileUserId);
        Assert.Equal(string.Empty, s.ProfileDisplayName);
        Assert.Null(s.AlbumId);
        Assert.Equal(string.Empty, s.AlbumName);
        Assert.Equal(Screen.Albums, s.AlbumReturn);
    }
}
