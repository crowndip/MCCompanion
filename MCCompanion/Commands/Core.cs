// Commands/Core.cs  –  ICommand + shared helpers

using System.Diagnostics;
using Terminal.Gui;

namespace MCCompanion.Commands;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface ICommand
{
    void Execute(string[] args);
}

// ── Path helpers ──────────────────────────────────────────────────────────────

internal static class PathHelper
{
    /// <summary>
    /// Normalise path separators to the platform convention and trim trailing slash.
    /// MC on Windows passes forward-slashes; on Linux it passes native separators.
    /// </summary>
    public static string Normalize(string path)
    {
        path = path.Replace('/', Path.DirectorySeparatorChar)
                   .Replace('\\', Path.DirectorySeparatorChar);
        return path.TrimEnd(Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Build absolute paths from MC args:
    ///   args[0]   = directory (%d)
    ///   args[1..] = filenames (%t split into individual args, or single %f)
    /// </summary>
    public static string[] BuildPaths(string[] args)
    {
        if (args.Length == 0) return [];
        if (args.Length == 1) return [Normalize(args[0])];
        string dir = Normalize(args[0]);
        return args[1..].Select(f => Path.Combine(dir, f)).ToArray();
    }
}

// ── Clipboard ─────────────────────────────────────────────────────────────────

internal static class ClipboardHelper
{
    /// <summary>
    /// Set system clipboard text.
    /// Terminal.Gui's Clipboard class handles xclip/xsel/wl-clipboard on Linux
    /// and Win32 on Windows automatically.
    /// Falls back to a process-based approach if TGui clipboard isn't available.
    /// </summary>
    public static bool TrySet(string text)
    {
        // Terminal.Gui clipboard (works cross-platform when a clipboard tool is present)
        if (Clipboard.IsSupported)
        {
            Clipboard.Contents = text;
            return true;
        }

        // Linux fallback: try xclip, xsel, wl-copy in order
        if (OperatingSystem.IsLinux())
        {
            string[][] tools =
            [
                ["xclip",   "-selection", "clipboard"],
                ["xsel",    "--clipboard", "--input"],
                ["wl-copy", "--"],
            ];
            foreach (var tool in tools)
            {
                if (TryPipeProcess(tool[0], tool[1..], text))
                    return true;
            }
        }

        return false;
    }

    private static bool TryPipeProcess(string exe, string[] extraArgs, string input)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true,
                UseShellExecute       = false,
            };
            foreach (var a in extraArgs) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.StandardInput.Write(input);
            proc.StandardInput.Close();
            proc.WaitForExit(2000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }
}

// ── Process launcher ──────────────────────────────────────────────────────────

internal static class ProcessHelper
{
    /// <summary>Try each (exe, args) pair in order; return true on first success.</summary>
    public static bool TryLaunch(params (string exe, string args)[] candidates)
    {
        foreach (var (exe, arguments) in candidates)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch { /* try next */ }
        }
        return false;
    }

    /// <summary>
    /// Launch exe with an individual argument list.
    /// On Windows, UseShellExecute=true is required to resolve .cmd/.bat wrappers
    /// (e.g. VS Code's 'code.cmd') and arguments are built into a quoted string.
    /// On Linux, UseShellExecute=true + ArgumentList throws InvalidOperationException,
    /// so we use UseShellExecute=false + ArgumentList for proper per-arg quoting.
    /// </summary>
    public static bool TryLaunchArgs(string exe, string workDir, params string[] argList)
    {
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                // Windows: shell-execute handles .cmd/.bat/.exe via PATHEXT.
                // Build a quoted argument string (double-quotes + backslash escaping).
                string args = string.Join(" ", argList.Select(QuoteWin));
                psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute  = true,
                    WorkingDirectory = workDir
                };
            }
            else
            {
                // Linux/macOS: UseShellExecute=false allows ArgumentList use.
                psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute  = false,
                    WorkingDirectory = workDir
                };
                foreach (var a in argList) psi.ArgumentList.Add(a);
            }
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Windows-style argument quoting per the CommandLineToArgvW rules:
    /// - Backslashes before a double-quote must be doubled, then the quote escaped with \
    /// - Trailing backslashes before the closing quote must be doubled
    /// - Args with no spaces, tabs, or quotes are returned unquoted
    /// </summary>
    private static string QuoteWin(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (!arg.Any(c => c is ' ' or '\t' or '"')) return arg;

        var sb = new System.Text.StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                // 2n+1 backslashes followed by an escaped quote
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
            }
            else
            {
                sb.Append('\\', backslashes);
                sb.Append(c);
                backslashes = 0;
            }
        }
        // Trailing backslashes precede the closing quote — must be doubled
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}

// ── Unknown verb ──────────────────────────────────────────────────────────────

internal class UnknownCommand(string verb) : ICommand
{
    public void Execute(string[] args)
    {
        Console.Error.WriteLine($"Unknown verb: \"{verb}\". Run MCCompanion without arguments for usage.");
        Environment.Exit(1);
    }
}
