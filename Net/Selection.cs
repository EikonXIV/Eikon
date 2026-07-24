using Eikon.Navigation;

namespace Eikon.Net;

// Carries the current selection across screens (the router navigates by screen only). Set before
// navigating to profile detail or a chat.
internal sealed class Selection
{
    public Guid? ProfileUserId { get; set; }

    public string ProfileDisplayName { get; set; } = string.Empty;

    // Where profile detail returns on back, captured at entry: a profile opened from a chat goes back
    // to that chat, from favorites back to favorites, from the grid back to the grid.
    public Screen ProfileReturn { get; set; } = Screen.Grid;

    // The album being viewed or edited (album detail, viewer, access sheet). Name is a snapshot for the
    // header before the album list loads.
    public Guid? AlbumId { get; set; }

    public string AlbumName { get; set; } = string.Empty;

    // Where album detail/viewer returns on back, captured at entry: an album opened from a chat goes
    // back to that chat, from a profile back to the profile, from the album list back to the list.
    public Screen AlbumReturn { get; set; } = Screen.Albums;
}
