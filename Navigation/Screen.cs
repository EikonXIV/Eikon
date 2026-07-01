namespace Eikon.Navigation;

// Top level destinations. Sub flows (onboarding steps, the filter sheet, editor sheets) are
// handled inside their owning screen rather than as separate router entries.
internal enum Screen
{
    AgeGuidelines,
    Onboarding,
    Unlock,
    Grid,
    Filter,
    ProfileDetail,
    Messages,
    Chat,
    SharedMedia,
    MyProfile,
    Settings,
    Favorites,
    Guidelines,
    Blocked,
    Albums,
    AlbumDetail,
    AlbumRequests,
    AlbumAccess,
    AlbumViewer,
}
