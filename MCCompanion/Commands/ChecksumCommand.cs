// Commands/ChecksumCommand.cs  –  cross-platform (pure .NET crypto)

using System.Security.Cryptography;
using MCCompanion.UI;
using Terminal.Gui;

namespace MCCompanion.Commands;

internal class ChecksumCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("Usage: MCCompanion checksum <file>"); return; }
        string path = PathHelper.Normalize(args[0]);

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return;
        }

        Tui.Run(() => new ChecksumDialog(path));
    }
}

internal class ChecksumDialog : Dialog
{
    private readonly string _path;
    private Label _lblMd5    = null!;
    private Label _lblSha1   = null!;
    private Label _lblSha256 = null!;

    public ChecksumDialog(string path) : base("Checksums", 70, 14)
    {
        _path = path;
        ColorScheme = Tui.McColors;
        Build();
        // Start computation after the dialog is shown
        Application.MainLoop.AddIdle(() => { _ = ComputeAsync(); return false; });
    }

    private void Build()
    {
        string filename = Path.GetFileName(_path);

        var lblFile = new Label($"File: {Truncate(filename, 60)}")
        { X = 1, Y = 1, Width = Dim.Fill(1) };

        var lblMd5Label    = MakeLabel("MD5:    ", 3);
        var lblSha1Label   = MakeLabel("SHA-1:  ", 4);
        var lblSha256Label = MakeLabel("SHA-256:", 5);

        _lblMd5    = MakeValue("Computing…", 3);
        _lblSha1   = MakeValue("Computing…", 4);
        _lblSha256 = MakeValue("Computing…", 5);

        // Use fixed X positions – Frame.Width is 0 before layout runs
        var btnCopyMd5    = MakeCopyBtn("Copy _MD5",    1,  () => Copy(_lblMd5.Text?.ToString()));
        var btnCopySha1   = MakeCopyBtn("Copy _SHA-1",  14, () => Copy(_lblSha1.Text?.ToString()));
        var btnCopySha256 = MakeCopyBtn("Copy S_HA-256", 28, () => Copy(_lblSha256.Text?.ToString()));

        var btnClose = new Button("_Close") { X = Pos.Center(), Y = 10 };
        btnClose.Clicked += () => Application.RequestStop();
        AddButton(btnClose);

        Add(lblFile, lblMd5Label, lblSha1Label, lblSha256Label,
            _lblMd5, _lblSha1, _lblSha256,
            btnCopyMd5, btnCopySha1, btnCopySha256);
    }

    private static Label MakeLabel(string text, int row)
        => new(text) { X = 1, Y = row, Width = 9 };

    private static Label MakeValue(string text, int row)
        => new(text) { X = 10, Y = row, Width = Dim.Fill(1) };

    private static Button MakeCopyBtn(string label, int x, Action action)
    {
        var b = new Button(label) { X = x, Y = 8 };
        b.Clicked += action;
        return b;
    }

    private void Copy(string? text)
    {
        if (string.IsNullOrEmpty(text) || text == "Computing…") return;
        ClipboardHelper.TrySet(text);
        Tui.Message("Copied", $"Copied to clipboard:\n{Truncate(text, 50)}");
    }

    private async Task ComputeAsync()
    {
        try
        {
            var md5Task    = HashFileAsync<MD5>(   _path);
            var sha1Task   = HashFileAsync<SHA1>(  _path);
            var sha256Task = HashFileAsync<SHA256>(_path);
            await Task.WhenAll(md5Task, sha1Task, sha256Task);

            // Extract results before entering the sync Invoke callback
            string md5    = md5Task.Result;
            string sha1   = sha1Task.Result;
            string sha256 = sha256Task.Result;

            Application.MainLoop?.Invoke(() =>
            {
                _lblMd5.Text    = md5;
                _lblSha1.Text   = sha1;
                _lblSha256.Text = sha256;
                Application.Refresh();
            });
        }
        catch (Exception ex)
        {
            string msg = ex.Message;
            Application.MainLoop?.Invoke(() =>
            {
                _lblMd5.Text = _lblSha1.Text = _lblSha256.Text = $"Error: {msg}";
                Application.Refresh();
            });
        }
    }

    private static async Task<string> HashFileAsync<TAlg>(string path) where TAlg : HashAlgorithm
    {
        using TAlg alg = (TAlg)Activator.CreateInstance(typeof(TAlg))!;
        using var s    = File.OpenRead(path);
        byte[] hash    = await alg.ComputeHashAsync(s).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
