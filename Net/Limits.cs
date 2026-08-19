namespace Eikon.Net;

// Client-side mirror of the request caps in contracts/src/dtos.ts. The server rejects a payload that
// breaks these with a bare 400, so every input that feeds a request must be bounded here first; keep
// this file in step with the contract when a cap changes.
internal static class Limits
{
    // SaveProfileRequest
    public const int DisplayNameMax = 20;
    public const int RacesMin = 1;
    public const int RacesMax = 4;
    public const int TribesMax = 8;
    public const int BioMax = 300;

    // CreateAlbumRequest / UpdateAlbumRequest
    public const int AlbumNameMax = 60;

    // CreateEventRequest / UpdateEventRequest / EventVenueDto
    public const int EventTitleMax = 80;
    public const int EventDescriptionMax = 1000;
    public const int EventTagsMax = 12;
    public const int EventDiscordNoteMax = 80;

    // DeleteAccountRequest
    public const int DeleteNoteMax = 1000;

    // Truncate to at most `max` UTF-16 units (what the server's z.string().max counts), never
    // splitting a surrogate pair. max <= 0 means unbounded.
    public static string Clamp(string value, int max)
    {
        if (max <= 0 || value.Length <= max)
            return value;
        var cut = max;
        if (char.IsHighSurrogate(value[cut - 1]))
            cut--;
        return value[..cut];
    }
}
