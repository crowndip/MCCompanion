// Commands/DiffViewCommand.cs
// Inline TUI side-by-side diff viewer.
// Pure .NET LCS algorithm — no external 'diff' binary required.
// Works identically on Windows and Linux.

using MCCompanion.UI;
using Terminal.Gui;

namespace MCCompanion.Commands;

internal class DiffViewCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: MCCompanion diff-view <file1> <file2>");
            return;
        }

        string f1 = PathHelper.Normalize(args[0]);
        string f2 = PathHelper.Normalize(args[1]);

        if (!File.Exists(f1)) { Console.Error.WriteLine($"File not found: {f1}"); return; }
        if (!File.Exists(f2)) { Console.Error.WriteLine($"File not found: {f2}"); return; }

        Tui.Run(() => new DiffViewDialog(f1, f2));
    }
}

// ── Data model ────────────────────────────────────────────────────────────────

internal enum RowKind { Context, Removed, Added, Changed, Separator }

internal readonly record struct ParallelRow(
    int?    LeftNo,
    string  LeftText,
    int?    RightNo,
    string  RightText,
    RowKind Kind
);

// ── Flat diff operation (internal intermediate) ───────────────────────────────

internal enum DiffOp { Context, Added, Removed }

// ── Dialog ────────────────────────────────────────────────────────────────────

internal sealed class DiffViewDialog : Dialog
{
    private readonly List<ParallelRow> _rows;
    private readonly int[]             _hunkPos;   // indices of Separator rows
    private          ListView          _lv    = null!;
    private          int               _hunk;

    public DiffViewDialog(string file1, string file2)
        : base(
            $" {TruncMid(Path.GetFileName(file1), 30)} ↔ {TruncMid(Path.GetFileName(file2), 30)} ",
            Application.Driver.Cols,
            Application.Driver.Rows)
    {
        ColorScheme = Tui.McColors;
        _rows    = BuildParallelDiff(file1, file2);
        _hunkPos = _rows
            .Select((r, i) => (r.Kind, i))
            .Where(x => x.Kind == RowKind.Separator)
            .Select(x => x.i)
            .ToArray();

        Build(file1, file2);

        // Auto-scroll to the first hunk separator
        if (_hunkPos.Length > 0)
            _lv.SelectedItem = _hunkPos[0];
    }

    private static string TruncMid(string s, int max) =>
        s.Length <= max ? s : s[..(max / 2)] + "…" + s[^(max / 2)..];

    // ── UI ────────────────────────────────────────────────────────────────────

    private void Build(string file1, string file2)
    {
        // Stats bar
        int added   = _rows.Count(r => r.Kind is RowKind.Added   or RowKind.Changed);
        int removed = _rows.Count(r => r.Kind is RowKind.Removed or RowKind.Changed);
        int context = _rows.Count(r => r.Kind == RowKind.Context);

        string stats = (added + removed) == 0
            ? "  Files are identical."
            : $"  +{added} added   -{removed} removed   {context} unchanged";

        Add(new Label(stats) { X = 1, Y = 1 });

        // Column headers — left filename | right filename
        string n1 = TruncMid(Path.GetFileName(file1), 36);
        string n2 = TruncMid(Path.GetFileName(file2), 36);
        Add(new Label($" ← {n1}") { X = 1,                  Y = 2 });
        Add(new Label($" {n2} →") { X = Pos.Percent(50) + 1, Y = 2 });

        // Diff list
        _lv = new ListView
        {
            X      = 1,
            Y      = 3,
            Width  = Dim.Fill(1),
            Height = Dim.Fill(2),
        };
        _lv.Source = new DiffSource(_rows);
        Add(_lv);

        // Buttons
        var btnClose = new Button("_Close") { IsDefault = true };
        btnClose.Clicked += () => Application.RequestStop();

        var btnPrev = new Button("_Prev hunk");
        btnPrev.Clicked += () => JumpHunk(-1);

        var btnNext = new Button("_Next hunk");
        btnNext.Clicked += () => JumpHunk(+1);

        AddButton(btnPrev);
        AddButton(btnNext);
        AddButton(btnClose);
    }

    private void JumpHunk(int delta)
    {
        if (_hunkPos.Length == 0) return;
        _hunk = Math.Clamp(_hunk + delta, 0, _hunkPos.Length - 1);
        _lv.SelectedItem = _hunkPos[_hunk];
        Application.Refresh();
    }

    // ── Diff engine ───────────────────────────────────────────────────────────

    private static List<ParallelRow> BuildParallelDiff(string file1, string file2)
    {
        if (IsBinary(file1) || IsBinary(file2))
            return [new(null, "  Binary files — no text diff available.", null, "", RowKind.Separator)];

        string[] left  = File.ReadAllLines(file1);
        string[] right = File.ReadAllLines(file2);

        const int Limit = 2000;
        if (left.Length > Limit || right.Length > Limit)
            return [new(null,
                $"  Files too large for inline diff ({left.Length} / {right.Length} lines).",
                null, "", RowKind.Separator)];

        if (left.SequenceEqual(right))
            return [];

        var flat = LcsDiff(left, right);
        var para = ToParallel(flat);
        return ExtractParallelHunks(para, context: 3);
    }

    private static bool IsBinary(string path)
    {
        try
        {
            Span<byte> buf = stackalloc byte[512];
            using var fs = File.OpenRead(path);
            int n = fs.Read(buf);
            for (int i = 0; i < n; i++)
                if (buf[i] == 0) return true;
            return false;
        }
        catch { return false; }
    }

    /// <summary>O(m·n) LCS back-track producing flat Context/Added/Removed ops.</summary>
    private static List<(DiffOp Op, string Text)> LcsDiff(string[] left, string[] right)
    {
        int m = left.Length, n = right.Length;

        // DP table (suffix-LCS)
        var dp = new int[m + 1, n + 1];
        for (int i = m - 1; i >= 0; i--)
            for (int j = n - 1; j >= 0; j--)
                dp[i, j] = left[i] == right[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        // Back-track
        var result = new List<(DiffOp, string)>(m + n);
        int li = 0, ri = 0;
        while (li < m || ri < n)
        {
            if (li < m && ri < n && left[li] == right[ri])
            {
                result.Add((DiffOp.Context, left[li])); li++; ri++;
            }
            else if (ri < n && (li >= m || dp[li, ri + 1] >= dp[li + 1, ri]))
            {
                result.Add((DiffOp.Added, right[ri])); ri++;
            }
            else
            {
                result.Add((DiffOp.Removed, left[li])); li++;
            }
        }
        return result;
    }

    /// <summary>
    /// Converts the flat LCS output into parallel (side-by-side) rows.
    /// Consecutive Removed/Added runs are paired positionally so they
    /// appear on the same row with their line numbers.
    /// </summary>
    private static List<ParallelRow> ToParallel(List<(DiffOp Op, string Text)> flat)
    {
        var rows = new List<ParallelRow>(flat.Count);
        int li = 1, ri = 1;
        int i = 0;

        while (i < flat.Count)
        {
            if (flat[i].Op == DiffOp.Context)
            {
                rows.Add(new(li, flat[i].Text, ri, flat[i].Text, RowKind.Context));
                li++; ri++;
                i++;
            }
            else
            {
                // Collect the full run of Removed/Added lines before the next Context
                var rem = new List<(int No, string Text)>();
                var add = new List<(int No, string Text)>();
                while (i < flat.Count && flat[i].Op != DiffOp.Context)
                {
                    if (flat[i].Op == DiffOp.Removed) { rem.Add((li, flat[i].Text)); li++; }
                    else                               { add.Add((ri, flat[i].Text)); ri++; }
                    i++;
                }

                // Pair removed and added lines positionally
                int pairs = Math.Max(rem.Count, add.Count);
                for (int k = 0; k < pairs; k++)
                {
                    bool hasL = k < rem.Count;
                    bool hasR = k < add.Count;
                    RowKind rk = (hasL && hasR) ? RowKind.Changed
                               : hasL           ? RowKind.Removed
                               :                  RowKind.Added;
                    rows.Add(new(
                        hasL ? rem[k].No : null, hasL ? rem[k].Text : "",
                        hasR ? add[k].No : null, hasR ? add[k].Text : "",
                        rk));
                }
            }
        }
        return rows;
    }

    /// <summary>
    /// Collapses long runs of context; keeps ±<paramref name="context"/> lines
    /// around each changed region and inserts @@ separator rows.
    /// </summary>
    private static List<ParallelRow> ExtractParallelHunks(List<ParallelRow> rows, int context)
    {
        var changed = rows
            .Select((r, i) => (r.Kind, i))
            .Where(x => x.Kind != RowKind.Context)
            .Select(x => x.i)
            .ToList();

        if (changed.Count == 0) return [];

        // Merge overlapping context windows
        var ranges = new List<(int S, int E)>();
        int rs = Math.Max(0, changed[0] - context);
        int re = Math.Min(rows.Count - 1, changed[0] + context);

        for (int i = 1; i < changed.Count; i++)
        {
            int ns = Math.Max(0, changed[i] - context);
            if (ns <= re + 1)
                re = Math.Min(rows.Count - 1, changed[i] + context);
            else
            {
                ranges.Add((rs, re));
                rs = ns;
                re = Math.Min(rows.Count - 1, changed[i] + context);
            }
        }
        ranges.Add((rs, re));

        // Emit separator + content for each range
        var result = new List<ParallelRow>();
        foreach (var (s, e) in ranges)
        {
            int na = 0, nr = 0;
            for (int i = s; i <= e; i++)
            {
                if (rows[i].Kind is RowKind.Added   or RowKind.Changed) na++;
                if (rows[i].Kind is RowKind.Removed or RowKind.Changed) nr++;
            }
            // First left line number in this range for the header
            int? ls = null;
            for (int i = s; i <= e && !ls.HasValue; i++)
                ls = rows[i].LeftNo;

            string header = ls.HasValue
                ? $" @@ -{nr} +{na}  line {ls} @@"
                : $" @@ -{nr} +{na} @@";

            result.Add(new(null, header, null, "", RowKind.Separator));
            for (int i = s; i <= e; i++)
                result.Add(rows[i]);
        }
        return result;
    }
}

// ── Two-panel colored list source ─────────────────────────────────────────────

internal sealed class DiffSource : IListDataSource
{
    private readonly List<ParallelRow> _rows;

    public DiffSource(List<ParallelRow> rows) => _rows = rows;

    public int  Count                         => _rows.Count;
    public int  Length                        => 0;   // disable horizontal scroll
    public bool IsMarked(int item)            => false;
    public void SetMark(int item, bool value) { }
    public System.Collections.IList ToList()  => _rows;

    // Line-number field width: "1234 " = 5 chars (4 digits + 1 space)
    private const int LineNoW = 5;

    public void Render(
        ListView container, ConsoleDriver driver,
        bool marked, int item, int col, int line, int width, int start = 0)
    {
        if (item < 0 || item >= _rows.Count) return;

        var row  = _rows[item];
        var norm = container.ColorScheme.Normal;

        // ── Full-width separator row (hunk header) ────────────────────────
        if (row.Kind == RowKind.Separator)
        {
            var attr = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Blue);
            DrawFull(driver, row.LeftText, attr, norm, col, width);
            return;
        }

        // ── Side-by-side row ──────────────────────────────────────────────
        int leftW  = (width - 1) / 2;
        int rightW = width - leftW - 1;   // absorbs odd-width terminals

        bool leftFiller  = row.Kind == RowKind.Added;
        bool rightFiller = row.Kind == RowKind.Removed;

        string leftStr  = BuildPanel(row.LeftNo,  row.LeftText,  leftW,  leftFiller);
        string rightStr = BuildPanel(row.RightNo, row.RightText, rightW, rightFiller);

        var fillerAttr = new Terminal.Gui.Attribute(Color.Gray, Color.Blue);

        var leftAttr = row.Kind switch
        {
            RowKind.Removed or RowKind.Changed => new Terminal.Gui.Attribute(Color.BrightRed,   Color.Blue),
            RowKind.Added                      => fillerAttr,
            _                                  => norm,
        };
        var rightAttr = row.Kind switch
        {
            RowKind.Added   or RowKind.Changed => new Terminal.Gui.Attribute(Color.BrightGreen, Color.Blue),
            RowKind.Removed                    => fillerAttr,
            _                                  => norm,
        };

        int skip = col, rem = width;
        DrawSegment(driver, leftStr,  leftAttr,  ref skip, ref rem);
        DrawSegment(driver, "│",      norm,       ref skip, ref rem);
        DrawSegment(driver, rightStr, rightAttr, ref skip, ref rem);
        if (rem > 0) { driver.SetAttribute(norm); driver.AddStr(new string(' ', rem)); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Build a single panel string of exactly <paramref name="panelWidth"/> chars.</summary>
    private static string BuildPanel(int? lineNo, string text, int panelWidth, bool filler)
    {
        if (panelWidth <= 0)     return "";
        if (panelWidth < LineNoW) return new string(' ', panelWidth);

        int textW = panelWidth - LineNoW;

        string no   = (filler || !lineNo.HasValue) ? "     " : $"{lineNo,4} ";
        string body = filler              ? new string(' ', textW)
                    : text.Length <= textW ? text.PadRight(textW)
                    :                        text[..textW];
        return no + body;
    }

    /// <summary>Render a full-width string (used for separator rows).</summary>
    private static void DrawFull(ConsoleDriver driver, string text,
                                  Terminal.Gui.Attribute attr, Terminal.Gui.Attribute fillAttr,
                                  int col, int width)
    {
        int skip = Math.Min(col, text.Length);
        string visible = skip < text.Length ? text[skip..] : "";
        if (visible.Length > width) visible = visible[..width];
        int pad = width - visible.Length;
        driver.SetAttribute(attr);
        driver.AddStr(visible);
        if (pad > 0) { driver.SetAttribute(fillAttr); driver.AddStr(new string(' ', pad)); }
    }

    /// <summary>Draw one segment of a row, honouring the horizontal-scroll offset.</summary>
    private static void DrawSegment(ConsoleDriver driver, string text,
                                     Terminal.Gui.Attribute attr,
                                     ref int skip, ref int remaining)
    {
        if (remaining <= 0) return;
        if (skip >= text.Length) { skip -= text.Length; return; }

        string visible = text[skip..];
        skip = 0;
        if (visible.Length > remaining) visible = visible[..remaining];

        driver.SetAttribute(attr);
        driver.AddStr(visible);
        remaining -= visible.Length;
    }
}
