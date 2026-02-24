// Commands/ClipboardCommand.cs
// Copies filenames or full paths to clipboard.
// Cross-platform: uses Terminal.Gui Clipboard (which wraps xclip/xsel/wl-copy on Linux).

namespace MCCompanion.Commands;

public enum ClipboardMode { Directory, Paths, Names }

internal class ClipboardCommand(ClipboardMode mode) : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return; }

        string text = mode switch
        {
            ClipboardMode.Directory => GetDir(args),
            ClipboardMode.Paths     => GetPaths(args),
            ClipboardMode.Names     => GetNames(args),
            _                       => string.Empty
        };

        if (string.IsNullOrEmpty(text))
        {
            Console.Error.WriteLine("MCCompanion clip: nothing to copy.");
            return;
        }

        bool ok = ClipboardHelper.TrySet(text);
        if (ok)
        {
            // Print confirmation to stderr so MC can optionally display it,
            // without polluting stdout which MC might capture.
            var lines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            string summary = lines.Length == 1
                ? $"Copied: {Truncate(lines[0], 70)}"
                : $"Copied {lines.Length} items to clipboard";
            Console.Error.WriteLine(summary);
        }
        else
        {
            Console.Error.WriteLine("Clipboard not available. Install xclip or xsel (Linux) or check permissions.");
            // Write to stdout as fallback so the user can see the value
            Console.WriteLine(text);
        }
    }

    // ── builders ──────────────────────────────────────────────────────────────

    private static string GetDir(string[] args)
        => PathHelper.Normalize(args[0]);

    private static string GetPaths(string[] args)
    {
        if (args.Length == 1) return PathHelper.Normalize(args[0]);
        string dir = PathHelper.Normalize(args[0]);
        return string.Join("\n", args[1..].Select(f => Path.Combine(dir, f)));
    }

    private static string GetNames(string[] args)
    {
        if (args.Length == 1) return Path.GetFileName(PathHelper.Normalize(args[0]));
        return string.Join("\n", args[1..]);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : "…" + s[^(max - 1)..];

    private void PrintUsage()
    {
        string usage = mode switch
        {
            ClipboardMode.Directory => "MCCompanion clip-dir  <dir>",
            ClipboardMode.Paths     => "MCCompanion clip-path <dir> <file1> [file2...]",
            ClipboardMode.Names     => "MCCompanion clip-name <dir> <file1> [file2...]",
            _                       => "MCCompanion clip-*"
        };
        Console.Error.WriteLine($"Usage: {usage}");
    }
}
