using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

public class TravelServiceTests
{
    [Fact]
    public void StripHome_removes_the_home_data_center_and_normalises()
    {
        Assert.Equal(new[] { 1, 5 }, TravelService.StripHome(new[] { 5, 7, 1, 7, 0 }, 7));
        Assert.Equal(new[] { 1, 5, 7 }, TravelService.StripHome(new[] { 5, 7, 1 }, null));
        Assert.Empty(TravelService.StripHome(new[] { 7 }, 7));
    }
}
