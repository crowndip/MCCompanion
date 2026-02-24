// Commands/FileInfoCommands.cs
// AttributesCommand, DirSizeCommand, TouchCommand – all cross-platform.
// Uses Terminal.Gui v1.19 API (CheckBox ctor: string text, bool isChecked).
// On Linux:  H/R/S/A don't exist; we show Unix chmod permissions instead.

using MCCompanion.UI;
using Terminal.Gui;

namespace MCCompanion.Commands;

// ── Attributes ────────────────────────────────────────────────────────────────

internal class AttributesCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: MCCompanion attributes <path>"); return; }
        string path = PathHelper.Normalize(args[0]);

        if (!File.Exists(path) && !Directory.Exists(path))
        { Console.Error.WriteLine($"Not found: {path}"); return; }

        Tui.Run(() => OperatingSystem.IsWindows()
            ? new WindowsAttributesDialog(path)
            : new LinuxPermissionsDialog(path));
    }
}

// Windows: H / R / S / A checkboxes
internal class WindowsAttributesDialog : Dialog
{
    private readonly string _path;
    private readonly CheckBox _chkR, _chkH, _chkS, _chkA;

    public WindowsAttributesDialog(string path) : base("File Attributes", 44, 14)
    {
        _path = path;
        ColorScheme = Tui.McColors;
        var attrs = File.GetAttributes(path);

        Add(new Label(Path.GetFileName(path)) { X = 1, Y = 1 });

        // Terminal.Gui v1: CheckBox(string text, bool isChecked)
        _chkR = new CheckBox("[ R ] Read-only", attrs.HasFlag(FileAttributes.ReadOnly))  { X = 2, Y = 3 };
        _chkH = new CheckBox("[ H ] Hidden",    attrs.HasFlag(FileAttributes.Hidden))    { X = 2, Y = 4 };
        _chkS = new CheckBox("[ S ] System",    attrs.HasFlag(FileAttributes.System))    { X = 2, Y = 5 };
        _chkA = new CheckBox("[ A ] Archive",   attrs.HasFlag(FileAttributes.Archive))   { X = 2, Y = 6 };

        var btnOk = new Button("_Apply") { IsDefault = true };
        btnOk.Clicked += Apply;
        var btnCancel = new Button("_Cancel");
        btnCancel.Clicked += () => Application.RequestStop();
        AddButton(btnCancel);
        AddButton(btnOk);
        Add(_chkR, _chkH, _chkS, _chkA);
    }

    private void Apply()
    {
        FileAttributes a = 0;
        if (_chkR.Checked) a |= FileAttributes.ReadOnly;
        if (_chkH.Checked) a |= FileAttributes.Hidden;
        if (_chkS.Checked) a |= FileAttributes.System;
        if (_chkA.Checked) a |= FileAttributes.Archive;
        if (a == 0) a = FileAttributes.Normal;
        try { File.SetAttributes(_path, a); Application.RequestStop(); }
        catch (Exception ex) { Tui.Message("Error", ex.Message); }
    }
}

// Linux: show octal mode + user/group/other rwx checkboxes
internal class LinuxPermissionsDialog : Dialog
{
    private readonly string _path;
    private readonly CheckBox[] _cbs = new CheckBox[9];

    public LinuxPermissionsDialog(string path) : base("Unix Permissions", 46, 14)
    {
        _path = path;
        ColorScheme = Tui.McColors;
        uint mode = GetMode(path);

        Add(new Label(Path.GetFileName(path)) { X = 1, Y = 1 });
        Add(new Label("      User  Group  Other") { X = 1, Y = 3 });
        Add(new Label("Read  ") { X = 1, Y = 5 });
        Add(new Label("Write ") { X = 1, Y = 6 });
        Add(new Label("Exec  ") { X = 1, Y = 7 });

        // Bits: 8=ur 7=uw 6=ux 5=gr 4=gw 3=gx 2=or 1=ow 0=ox
        // Display as rows=rwx, cols=user/group/other
        int[] xPos = [9, 16, 23];
        int[] yPos = [5, 6, 7];
        // index mapping: row 0 = read bits (8,5,2), row 1 = write (7,4,1), row 2 = exec (6,3,0)
        int[][] bitMap = [[8, 5, 2], [7, 4, 1], [6, 3, 0]];

        int cbIdx = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int bit = bitMap[row][col];
                bool set = (mode & (1u << bit)) != 0;
                _cbs[cbIdx] = new CheckBox("[ ]", set)
                {
                    X = xPos[col],
                    Y = yPos[row]
                };
                cbIdx++;
            }
        }

        var btnOk = new Button("_Apply") { IsDefault = true };
        btnOk.Clicked += Apply;
        var btnCancel = new Button("_Cancel");
        btnCancel.Clicked += () => Application.RequestStop();
        AddButton(btnCancel);
        AddButton(btnOk);
        foreach (var cb in _cbs) Add(cb);
    }

    private void Apply()
    {
        // Reconstruct mode from checkbox positions
        int[][] bitMap = [[8, 5, 2], [7, 4, 1], [6, 3, 0]];
        uint mode = 0;
        int idx = 0;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                if (_cbs[idx].Checked) mode |= 1u << bitMap[row][col];
                idx++;
            }
        try { Chmod(_path, mode); Application.RequestStop(); }
        catch (Exception ex) { Tui.Message("Error", ex.Message); }
    }

    private static uint GetMode(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("stat",
                $"-c %a \"{path}\"")
            { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi);
            string? line = p?.StandardOutput.ReadLine()?.Trim();
            p?.WaitForExit(2000);
            if (line != null && uint.TryParse(line,
                System.Globalization.NumberStyles.None,
                null, out uint _))
            {
                return Convert.ToUInt32(line, 8);
            }
        }
        catch { }
        return 0b_110_100_100; // sensible default
    }

    private static void Chmod(string path, uint mode)
    {
        string octal = Convert.ToString(mode, 8).PadLeft(4, '0');
        var psi = new System.Diagnostics.ProcessStartInfo("chmod",
            $"{octal} \"{path}\"")
        { UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi);
        p?.WaitForExit(3000);
    }
}

// ── Directory Size ────────────────────────────────────────────────────────────

internal class DirSizeCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: MCCompanion dir-size <dir>"); return; }
        string path = PathHelper.Normalize(args[0]);
        if (!Directory.Exists(path)) { Console.Error.WriteLine($"Directory not found: {path}"); return; }
        Tui.Run(() => new DirSizeDialog(path));
    }
}

internal class DirSizeDialog : Dialog
{
    private readonly string _path;
    private Label _lblSize  = null!;
    private Label _lblFiles = null!;
    private Label _lblDirs  = null!;

    public DirSizeDialog(string path) : base("Folder Size", 54, 12)
    {
        _path = path;
        ColorScheme = Tui.McColors;
        Build();
        Application.MainLoop.AddIdle(() => { _ = ComputeAsync(); return false; });
    }

    private void Build()
    {
        string name = Path.GetFileName(_path.TrimEnd(Path.DirectorySeparatorChar));
        Add(new Label(name) { X = 1, Y = 1 });

        Add(new Label("Size:    ") { X = 1, Y = 3 });
        _lblSize  = new Label("Calculating…") { X = 11, Y = 3, Width = 40 };

        Add(new Label("Files:   ") { X = 1, Y = 4 });
        _lblFiles = new Label("…") { X = 11, Y = 4 };

        Add(new Label("Folders: ") { X = 1, Y = 5 });
        _lblDirs  = new Label("…") { X = 11, Y = 5 };

        var btnCopy = new Button("Copy _Size");
        btnCopy.Clicked += () => { ClipboardHelper.TrySet(_lblSize.Text?.ToString() ?? ""); };
        var btnClose = new Button("_Close") { IsDefault = true };
        btnClose.Clicked += () => Application.RequestStop();
        AddButton(btnCopy);
        AddButton(btnClose);
        Add(_lblSize, _lblFiles, _lblDirs);
    }

    private async Task ComputeAsync()
    {
        var (bytes, files, dirs) = await Task.Run(() =>
        {
            long b = 0; int f = 0;
            foreach (var fi in new DirectoryInfo(_path)
                .EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { b += fi.Length; f++; } catch { /* access denied – skip */ }
            }
            int d = 0;
            try { d = Directory.GetDirectories(_path, "*", SearchOption.AllDirectories).Length; }
            catch { }
            return (b, f, d);
        }).ConfigureAwait(false);

        string sizeText  = FormatBytes(bytes);
        string fileText  = $"{files:N0}";
        string dirText   = $"{dirs:N0}";

        Application.MainLoop?.Invoke(() =>
        {
            _lblSize.Text  = sizeText;
            _lblFiles.Text = fileText;
            _lblDirs.Text  = dirText;
            Application.Refresh();
        });
    }

    private static string FormatBytes(long b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F2} {u[i]}  ({b:N0} bytes)";
    }
}

// ── Touch ─────────────────────────────────────────────────────────────────────

internal class TouchCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: MCCompanion touch <path>"); return; }
        string path = PathHelper.Normalize(args[0]);
        if (!File.Exists(path) && !Directory.Exists(path))
        { Console.Error.WriteLine($"Not found: {path}"); return; }
        Tui.Run(() => new TouchDialog(path));
    }
}

internal class TouchDialog : Dialog
{
    private readonly string _path;
    private readonly TextField _tfDate;
    private readonly TextField _tfTime;
    private readonly CheckBox  _chkM;
    private readonly CheckBox  _chkC;
    private readonly CheckBox  _chkA;

    public TouchDialog(string path) : base("Set Timestamp", 52, 15)
    {
        _path = path;
        ColorScheme = Tui.McColors;
        string now  = DateTime.Now.ToString("yyyy-MM-dd");
        string nowt = DateTime.Now.ToString("HH:mm:ss");

        Add(new Label(Path.GetFileName(path)) { X = 1, Y = 1 });

        Add(new Label("Date (yyyy-MM-dd): ") { X = 1, Y = 3 });
        _tfDate = new TextField(now)  { X = 20, Y = 3, Width = 12 };

        Add(new Label("Time (HH:mm:ss):  ") { X = 1, Y = 4 });
        _tfTime = new TextField(nowt) { X = 20, Y = 4, Width = 10 };

        // Terminal.Gui v1: CheckBox(string text, bool isChecked)
        _chkM = new CheckBox("Last _modified", true)  { X = 1, Y = 6 };
        _chkC = new CheckBox("_Created",       false) { X = 1, Y = 7,
            // Linux filesystems do not expose birth time via POSIX; setting it is a no-op.
            Enabled = OperatingSystem.IsWindows() };
        _chkA = new CheckBox("Last _accessed", false) { X = 1, Y = 8 };
        if (!OperatingSystem.IsWindows())
            Add(new Label("(not supported on Linux)") { X = 14, Y = 7 });

        var btnNow = new Button("_Now");
        btnNow.Clicked += () =>
        {
            _tfDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
            _tfTime.Text = DateTime.Now.ToString("HH:mm:ss");
        };
        var btnApply  = new Button("_Apply") { IsDefault = true };
        btnApply.Clicked += Apply;
        var btnCancel = new Button("_Cancel");
        btnCancel.Clicked += () => Application.RequestStop();
        AddButton(btnCancel);
        AddButton(btnNow);
        AddButton(btnApply);
        Add(_tfDate, _tfTime, _chkM, _chkC, _chkA);
    }

    private void Apply()
    {
        string ds = _tfDate.Text?.ToString() ?? string.Empty;
        string ts = _tfTime.Text?.ToString() ?? string.Empty;
        if (!DateTime.TryParse($"{ds} {ts}", out DateTime dt))
        {
            Tui.Message("Error", "Invalid date/time.\nExpected yyyy-MM-dd and HH:mm:ss.");
            return;
        }
        try
        {
            bool isDir = Directory.Exists(_path);
            if (_chkM.Checked)
                if (isDir) Directory.SetLastWriteTime(_path, dt);
                else File.SetLastWriteTime(_path, dt);
            if (_chkC.Checked)
                if (isDir) Directory.SetCreationTime(_path, dt);
                else File.SetCreationTime(_path, dt);
            if (_chkA.Checked)
                if (isDir) Directory.SetLastAccessTime(_path, dt);
                else File.SetLastAccessTime(_path, dt);
            Application.RequestStop();
        }
        catch (Exception ex) { Tui.Message("Error", ex.Message); }
    }
}
