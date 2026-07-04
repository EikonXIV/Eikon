using System.Text.Json;
using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// RelayClient.ParseAlbumNotice turns an incoming relay frame into an AlbumNotice, with fallbacks for
// the optional display-name fields.
public class RelayParseTests
{
    private static JsonElement Obj(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseAlbumNotice_reads_all_fields()
    {
        var from = Guid.NewGuid();
        var album = Guid.NewGuid();
        var n = RelayClient.ParseAlbumNotice(Obj(
            $"{{\"from\":\"{from}\",\"fromName\":\"Kai\",\"albumId\":\"{album}\",\"albumName\":\"Spicy Pics\"}}"));
        Assert.Equal(from, n.PeerId);
        Assert.Equal("Kai", n.PeerName);
        Assert.Equal(album, n.AlbumId);
        Assert.Equal("Spicy Pics", n.AlbumName);
    }

    [Fact]
    public void ParseAlbumNotice_falls_back_when_optional_names_are_absent()
    {
        var n = RelayClient.ParseAlbumNotice(Obj(
            $"{{\"from\":\"{Guid.NewGuid()}\",\"albumId\":\"{Guid.NewGuid()}\"}}"));
        Assert.Equal("Someone", n.PeerName);
        Assert.Equal("an album", n.AlbumName);
    }
}
