// Commands/ShellCommands.cs
// Context menu, Properties, Open-With.
// On Windows: native shell dialogs via P/Invoke (Platform/Windows/).
// On Linux:   xdg-open / nautilus --select equivalents.
//
// NOTE: The Platform/Windows/*.cs files are excluded from compilation on Linux
// via the .csproj ItemGroup condition. That means on Linux the
// MCCompanion.Platform.Windows namespace simply does not exist, so we must
// wrap every call to it in OperatingSystem.IsWindows() + a #if to prevent
// the Linux compiler from seeing the type reference.

using MCCompanion.UI;

namespace MCCompanion.Commands;

// ── Context menu / Reveal ────────────────────────────────────────────────────

internal class ContextCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: MCCompanion context <path>"); return; }
        string path = PathHelper.Normalize(args[0]);

        if (OperatingSystem.IsWindows())
        {
            PlatformWindows.ShowContextMenu(path);
        }
        else
        {
            // Linux: open the parent folder in the default file manager,
            // selecting (highlighting) the target file where supported.
            string parent = Path.GetDirectoryName(path) ?? path;
            bool ok =
                ProcessHelper.TryLaunchArgs("nautilus", parent, "--select", path) ||
                ProcessHelper.TryLaunchArgs("dolphin",  parent, "--select", path) ||
                ProcessHelper.TryLaunchArgs("nemo",     parent, path)             ||
                ProcessHelper.TryLaunchArgs("xdg-open", parent, parent);

            if (!ok)
                Console.Error.WriteLine($"Could not open file manager for: {path}");
        }
    }
}

// ── Properties ───────────────────────────────────────────────────────────────

internal class PropertiesCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: MCCompanion properties <path>"); return; }
        string path = PathHelper.Normalize(args[0]);

        if (OperatingSystem.IsWindows())
        {
            PlatformWindows.ShowProperties(path);
        }
        else
        {
            Tui.Run(() => new PropertiesDialog(path));
        }
    }
}

// ── Open With ────────────────────────────────────────────────────────────────

internal class OpenWithCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: MCCompanion open-with <path>"); return; }
        string path = PathHelper.Normalize(args[0]);

        if (OperatingSystem.IsWindows())
        {
            PlatformWindows.ShowOpenWith(path);
        }
        else
        {
            Tui.Run(() => new OpenWithDialog(path));
        }
    }
}

// ── Windows platform bridge ──────────────────────────────────────────────────
// Isolated in a partial static class so the Linux compiler never sees
// any reference to MCCompanion.Platform.Windows types.

internal static partial class PlatformWindows
{
    // Stubs on Linux – these methods are never called due to
    // OperatingSystem.IsWindows() guards above, but they must compile.
    static partial void ShowContextMenuImpl(string path);
    static partial void ShowPropertiesImpl(string path);
    static partial void ShowOpenWithImpl(string path);

    public static void ShowContextMenu(string path) => ShowContextMenuImpl(path);
    public static void ShowProperties(string path)  => ShowPropertiesImpl(path);
    public static void ShowOpenWith(string path)    => ShowOpenWithImpl(path);
}
