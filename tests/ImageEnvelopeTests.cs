using Eikon.Net;
using Xunit;

namespace Eikon.Tests;

// Receiving an image must never depend on the network. The envelope parse is pure, so a message always
// materializes (and so gets acked) even when the photo itself can't be fetched; the blob is downloaded
// later from the storage key and blob key this returns. A malformed payload returns null and the caller
// falls back to a plain-text message rather than dropping the message un-acked, which is what used to
// leave a notification and a badge with nothing behind them.
public class ImageEnvelopeTests
{
    private const string Magic = "img:";

    [Fact]
    public void Parses_a_full_envelope()
    {
        var env = ChatService.ParseImageEnvelope(Magic + """{"sk":"chat/abc.bin","k":"a2V5","nsfw":true,"cap":"look at this"}""");

        Assert.NotNull(env);
        Assert.Equal("chat/abc.bin", env!.Value.StorageKey);
        Assert.Equal("a2V5", env.Value.BlobKey);
        Assert.True(env.Value.Nsfw);
        Assert.Equal("look at this", env.Value.Caption);
    }

    [Fact]
    public void Optional_fields_default_when_absent()
    {
        var env = ChatService.ParseImageEnvelope(Magic + """{"sk":"chat/abc.bin","k":"a2V5"}""");

        Assert.NotNull(env);
        Assert.False(env!.Value.Nsfw);
        Assert.Equal(string.Empty, env.Value.Caption);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"k":"a2V5"}""")]          // no storage key
    [InlineData("""{"sk":"chat/abc.bin"}""")] // no blob key
    [InlineData("")]
    public void Malformed_envelopes_return_null(string payload)
    {
        Assert.Null(ChatService.ParseImageEnvelope(Magic + payload));
    }
}
