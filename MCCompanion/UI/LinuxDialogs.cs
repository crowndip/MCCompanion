// UI/LinuxDialogs.cs
// TUI dialogs used on Linux as replacements for Windows-only shell dialogs.

using Terminal.Gui;

namespace MCCompanion.UI;

// ── Properties panel (Linux) ──────────────────────────────────────────────────

internal class PropertiesDialog : Dialog
{
    public PropertiesDialog(string path) : base("Properties", 60, 18)
    {
        ColorScheme = Tui.McColors;
        bool isDir = Directory.Exists(path);
        var info   = isDir ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);

        int row = 1;
        void AddRow(string label, string value)
        {
            Add(new Label($"{label,-16}") { X = 1, Y = row });
            Add(new Label(value)          { X = 18, Y = row, Width = Dim.Fill(1) });
            row++;
        }

        AddRow("Name:",     info.Name);
        AddRow("Path:",     Truncate(info.FullName, 38));
        AddRow("Type:",     isDir ? "Directory" : "File");
        if (!isDir && info is FileInfo fi)
            AddRow("Size:",     FormatBytes(fi.Length));
        AddRow("Modified:", info.LastWriteTime.ToString("yyyy-MM-dd  HH:mm:ss"));
        AddRow("Created:",  info.CreationTime.ToString("yyyy-MM-dd  HH:mm:ss"));
        AddRow("Accessed:", info.LastAccessTime.ToString("yyyy-MM-dd  HH:mm:ss"));

        // Unix permissions via stat
        string perms = GetPermissions(path);
        if (!string.IsNullOrEmpty(perms))
        {
            AddRow("Permissions:", perms);
        }

        var btnClose = new Button("_Close") { IsDefault = true };
        btnClose.Clicked += () => Application.RequestStop();
        AddButton(btnClose);
    }

    private static string GetPermissions(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("stat", $"-c \"%A  %U:%G\" \"{path}\"")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi);
            string? line = p?.StandardOutput.ReadLine()?.Trim();
            p?.WaitForExit(2000);
            return line ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static string FormatBytes(long b)
    {
        string[] u = ["B", "KB", "MB", "GB"];
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F2} {u[i]}  ({b:N0} bytes)";
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : "…" + s[^(max - 1)..];
}

// ── Open-With input dialog (Linux) ────────────────────────────────────────────

internal class OpenWithDialog : Dialog
{
    private readonly string _path;
    private TextField _tf = null!;

    public OpenWithDialog(string path) : base("Open With", 56, 10)
    {
        _path = path;
        ColorScheme = Tui.McColors;
        Add(new Label($"File: {Truncate(Path.GetFileName(path), 45)}") { X = 1, Y = 1 });
        Add(new Label("Application:") { X = 1, Y = 3 });
        _tf = new TextField("") { X = 14, Y = 3, Width = Dim.Fill(1) };

        var btnOk = new Button("_Open") { IsDefault = true };
        btnOk.Clicked += Open;
        var btnCancel = new Button("_Cancel");
        btnCancel.Clicked += () => Application.RequestStop();
        AddButton(btnCancel);
        AddButton(btnOk);
        Add(_tf);
    }

    private void Open()
    {
        string app = _tf.Text?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(app))
        {
            // Fall back to xdg-open (default application)
            app = "xdg-open";
        }
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(app)
            {
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(_path);
            System.Diagnostics.Process.Start(psi);
            Application.RequestStop();
        }
        catch (Exception ex)
        {
            Tui.Message("Error", $"Could not open with \"{app}\":\n{ex.Message}");
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
