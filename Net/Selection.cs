namespace Eikon.Net;

// Carries the current selection across screens (the router navigates by screen only). Set before
// navigating to profile detail or a chat.
internal sealed class Selection
{
    public Guid? ProfileUserId { get; set; }

    public string ProfileDisplayName { get; set; } = string.Empty;

    // The album being viewed or edited (album detail, viewer, access sheet). Name is a snapshot for the
    // header before the album list loads.
    public Guid? AlbumId { get; set; }

    public string AlbumName { get; set; } = string.Empty;
}
