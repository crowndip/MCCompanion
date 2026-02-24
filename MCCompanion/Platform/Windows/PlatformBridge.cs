// Platform/Windows/PlatformBridge.cs
// Windows-only implementation of the PlatformWindows partial class.
// This file is excluded from compilation on Linux by the .csproj condition.

using System.Runtime.Versioning;

namespace MCCompanion.Commands;

[SupportedOSPlatform("windows")]
internal static partial class PlatformWindows
{
    static partial void ShowContextMenuImpl(string path)
        => MCCompanion.Platform.Windows.ShellContextMenu.Show(path);

    static partial void ShowPropertiesImpl(string path)
        => MCCompanion.Platform.Windows.ShellDialogs.ShowProperties(path);

    static partial void ShowOpenWithImpl(string path)
        => MCCompanion.Platform.Windows.ShellDialogs.ShowOpenWith(path);
}
