// Commands/UtilityCommands.cs
// TerminalCommand  – open a terminal in a directory
// CompareCommand   – diff two files
// Both are platform-aware: different tools on Windows vs Linux.

namespace MCCompanion.Commands;

// ── Terminal ──────────────────────────────────────────────────────────────────

internal class TerminalCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: MCCompanion terminal <dir>"); return; }
        string dir = PathHelper.Normalize(args[0]);
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"Directory not found: {dir}"); return; }

        bool ok;
        if (OperatingSystem.IsWindows())
        {
            // Windows Terminal → PowerShell 7 → PowerShell 5 → cmd
            ok = ProcessHelper.TryLaunchArgs("wt.exe",          dir, "-d", dir)
              || ProcessHelper.TryLaunchArgs("pwsh.exe",         dir, "-NoExit", "-Command", $"Set-Location '{dir}'")
              || ProcessHelper.TryLaunchArgs("powershell.exe",   dir, "-NoExit", "-Command", $"Set-Location '{dir}'")
              || ProcessHelper.TryLaunchArgs("cmd.exe",          dir, "/K", $"cd /d \"{dir}\"");
        }
        else
        {
            // Linux: try common terminal emulators in priority order
            // Each emulator has a different flag for "start in directory"
            ok = ProcessHelper.TryLaunchArgs("gnome-terminal", dir, "--working-directory", dir)
              || ProcessHelper.TryLaunchArgs("konsole",        dir, "--workdir", dir)
              || ProcessHelper.TryLaunchArgs("xfce4-terminal", dir, "--working-directory", dir)
              || ProcessHelper.TryLaunchArgs("lxterminal",     dir, "--working-directory", dir)
              || ProcessHelper.TryLaunchArgs("tilix",          dir, "--working-directory", dir)
              || ProcessHelper.TryLaunchArgs("alacritty",      dir, "--working-directory", dir)
              || ProcessHelper.TryLaunchArgs("kitty",          dir, "-d", dir)
              || ProcessHelper.TryLaunchArgs("xterm",          dir, "-e", "bash", "-c", $"cd \"{dir}\" && exec $SHELL");
        }

        if (!ok)
            Console.Error.WriteLine("Could not launch a terminal emulator. Install gnome-terminal, konsole, or xterm.");
    }
}

// ── Compare ───────────────────────────────────────────────────────────────────

internal class CompareCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: MCCompanion compare <file1> <file2>");
            return;
        }

        string f1 = PathHelper.Normalize(args[0]);
        string f2 = PathHelper.Normalize(args[1]);

        bool ok;
        if (OperatingSystem.IsWindows())
            ok = TryWindowsDiff(f1, f2);
        else
            ok = TryLinuxDiff(f1, f2);

        if (!ok)
            Console.Error.WriteLine($"Could not launch a diff tool for:\n  {f1}\n  {f2}");
    }

    private static bool TryWindowsDiff(string f1, string f2)
    {
        // WinMerge (check registry, then common paths)
        string? wm = FindWinMerge();
        if (wm != null)
            return ProcessHelper.TryLaunchArgs(wm, Path.GetDirectoryName(f1) ?? ".", f1, f2);

        // VS Code diff
        if (ProcessHelper.TryLaunchArgs("code", ".", "--diff", f1, f2)) return true;

        // Notepad++ compare plugin (if plugin exists this verb works)
        // Fallback: fc.exe in a cmd window
        return ProcessHelper.TryLaunchArgs("cmd.exe", ".", "/K", $"fc \"{f1}\" \"{f2}\"");
    }

    private static bool TryLinuxDiff(string f1, string f2)
    {
        // Meld – most popular GUI diff on Linux
        if (ProcessHelper.TryLaunchArgs("meld", ".", f1, f2)) return true;

        // KDE's kdiff3
        if (ProcessHelper.TryLaunchArgs("kdiff3", ".", f1, f2)) return true;

        // VS Code diff
        if (ProcessHelper.TryLaunchArgs("code", ".", "--diff", f1, f2)) return true;

        // vimdiff in a terminal — use ArgumentList so paths with spaces are safe
        if (ProcessHelper.TryLaunchArgs("gnome-terminal", ".", "--", "vimdiff", f1, f2)) return true;
        if (ProcessHelper.TryLaunchArgs("konsole",        ".", "-e", "vimdiff", f1, f2)) return true;
        if (ProcessHelper.TryLaunchArgs("xfce4-terminal", ".", "-e", $"vimdiff '{f1}' '{f2}'")) return true;
        if (ProcessHelper.TryLaunchArgs("xterm",          ".", "-e", "vimdiff", f1, f2)) return true;

        return false;
    }

    private static string? FindWinMerge()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // Try registry
        try
        {
#pragma warning disable CA1416
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Thingamahoochie\WinMerge");
            if (key?.GetValue("Executable") is string p && File.Exists(p)) return p;
#pragma warning restore CA1416
        }
        catch { }

        // Common install paths
        string[] paths =
        [
            @"C:\Program Files\WinMerge\WinMergeU.exe",
            @"C:\Program Files (x86)\WinMerge\WinMergeU.exe"
        ];
        return paths.FirstOrDefault(File.Exists);
    }
}
