namespace Eikon.Net;

// Carries the current selection across screens (the router navigates by screen only). Set before
// navigating to profile detail or a chat.
internal sealed class Selection
{
    public Guid? ProfileUserId { get; set; }

    public string ProfileDisplayName { get; set; } = string.Empty;
}
