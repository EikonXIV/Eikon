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

    // The event being viewed or edited (event detail, create/edit). Name is a snapshot for the header
    // before the detail loads.
    public Guid? EventId { get; set; }

    public string EventName { get; set; } = string.Empty;

    // Where event detail returns on back, captured at entry: an event opened from the board goes back to
    // the board (the Grid's Events tab), from a chat share card back to that chat.
    public Screen EventReturn { get; set; } = Screen.Grid;

    // An event queued to share into a conversation: set before switching to Messages so the picked chat
    // sends the event card. Cleared once sent.
    public Guid? PendingShareEventId { get; set; }
}
