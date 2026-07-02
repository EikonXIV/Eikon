namespace Eikon;

// Compile-time identity. A Debug build loads as a separate Dalamud plugin ("EikonLocal") with its own
// config, slash command, and window namespace, so local testing never collides with — or overwrites the
// config of — an installed release. The AssemblyName (and therefore Dalamud's InternalName) is switched
// in the csproj; these constants keep the command and window names in step. Release is plain "Eikon".
internal static class BuildInfo
{
#if DEBUG
    public const string DisplayName = "Eikon (Local)";
    public const string WindowNamespace = "EikonLocal";
    public const string Command = "/eikonlocal";
    public const bool IsLocal = true;
#else
    public const string DisplayName = "Eikon";
    public const string WindowNamespace = "Eikon";
    public const string Command = "/eikon";
    public const bool IsLocal = false;
#endif
}
