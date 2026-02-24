// Commands/BatchRenameCommand.cs  –  cross-platform (pure .NET File.Move)
// Uses Terminal.Gui v1.19 API.
//
// ┌─ Rename mask placeholders ─────────────────────────────────────────────┐
// │  [N]      entire original filename (no extension)                      │
// │  [N1-5]   characters 1–5 (1-indexed, inclusive)                        │
// │  [N-5]    last 5 characters of the name                                │
// │  [P]      immediate parent folder name                                  │
// │  [C]      counter  (uses global Start / Step / Digits settings)        │
// │  [C:3]    counter with inline digit-width (overrides global Digits)    │
// │  [C:A]    alphabetic counter  (A, B … Z, AA, AB …)                    │
// │  [Y][M][D] file modification year / month / day                        │
// │  [h][m][s] file modification hour / minute / second                    │
// │  [T]      current date+time at rename time  (yyyyMMdd_HHmmss)          │
// │  [G]      new GUID per file (32 hex chars, no dashes)                  │
// │  [E]      original extension without the leading dot                   │
// │  [U]      convert the preceding placeholder's result to UPPERCASE      │
// │  [c][L]   convert the preceding placeholder's result to lowercase      │
// └────────────────────────────────────────────────────────────────────────┘
//
// Search / Replace: use  |  to separate multiple pairs at once.
// RegEx mode: use capture groups ($1, $2 …) in the Replace field.

using System.Globalization;
using System.Text.RegularExpressions;
using MCCompanion.UI;
using Terminal.Gui;

namespace MCCompanion.Commands;

internal class BatchRenameCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: MCCompanion rename <dir>");
            return;
        }

        string dir = PathHelper.Normalize(args[0]);

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"Directory not found: {dir}");
            return;
        }

        string[] paths = Directory
            .GetFiles(dir)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
        {
            Console.Error.WriteLine("No files in directory.");
            return;
        }

        Tui.Run(() => new BatchRenameDialog(dir, paths));
    }
}

internal class BatchRenameDialog : Dialog
{
    private readonly string[] _paths;

    // ── Masks ────────────────────────────────────────────────────────────────
    private TextField _tfMask    = null!;   // rename mask   (default "[N]")
    private TextField _tfExtMask = null!;   // extension mask (default "[E]")

    // ── Search / Replace ─────────────────────────────────────────────────────
    private TextField _tfSearch  = null!;
    private TextField _tfReplace = null!;
    private CheckBox  _chkRegex  = null!;
    private CheckBox  _chkCase   = null!;   // case-sensitive search

    // ── Case conversion ───────────────────────────────────────────────────────
    private RadioGroup _rgCase   = null!;   // 0=no change, 1=UPPER, 2=lower, 3=Title

    // ── Counter ───────────────────────────────────────────────────────────────
    private TextField _tfStart  = null!;    // starting value
    private TextField _tfStep   = null!;    // increment per file
    private TextField _tfDigits = null!;    // minimum digit width (zero-padded)

    // ── Preview ───────────────────────────────────────────────────────────────
    private ListView  _preview  = null!;

    // ─────────────────────────────────────────────────────────────────────────

    public BatchRenameDialog(string dir, string[] paths)
        : base(
            $"Batch Rename  –  {Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar))}  ({paths.Length} files)",
            78, 36)
    {
        _paths = paths;
        ColorScheme = Tui.McColors;
        Build();
        UpdatePreview();
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void Build()
    {
        // ── Masks ─────────────────────────────────────────────────────────────
        //
        //  Rename mask:  [_____________________________________________]
        //  Extension:    [__________________]  (empty = keep original)
        //
        Add(new Label("Rename mask:") { X = 1, Y = 1, Width = 13 });
        _tfMask = new TextField("[N]") { X = 14, Y = 1, Width = 60 };
        _tfMask.TextChanged += _ => UpdatePreview();

        Add(new Label("Extension:") { X = 1, Y = 2, Width = 11 });
        _tfExtMask = new TextField("[E]") { X = 14, Y = 2, Width = 20 };
        _tfExtMask.TextChanged += _ => UpdatePreview();
        Add(new Label("(empty = keep original)") { X = 36, Y = 2 });

        // ── Placeholder reference (always visible) ────────────────────────────
        //
        //  Name: [N]  [N1-5]  [N-5]  [P]     Counter: [C]  [C:3]  [C:A]     Ext: [E]
        //  Date: [Y][M][D]  [h][m][s]    Now: [T]    GUID: [G]    Case: [U] [c][L]
        //
        Add(new Label("Name: [N]  [N1-5]  [N-5]  [P]     Counter: [C]  [C:3]  [C:A]     Ext: [E]") { X = 1, Y = 4 });
        Add(new Label("Date: [Y][M][D]  [h][m][s]    Now: [T]    GUID: [G]    Case: [U] [c][L]")    { X = 1, Y = 5 });

        // ── Search / Replace ──────────────────────────────────────────────────
        //
        //  Search:  [______________________]  Replace: [______________________]
        //           [ ] RegEx  [ ] Case-sensitive  (use | to separate pairs)
        //
        Add(new Label("Search:") { X = 1, Y = 7, Width = 8 });
        _tfSearch = new TextField("") { X = 10, Y = 7, Width = 24 };
        _tfSearch.TextChanged += _ => UpdatePreview();

        Add(new Label("Replace:") { X = 36, Y = 7, Width = 9 });
        _tfReplace = new TextField("") { X = 46, Y = 7, Width = 24 };
        _tfReplace.TextChanged += _ => UpdatePreview();

        _chkRegex = new CheckBox("RegEx",          false) { X = 10, Y = 8 };
        _chkCase  = new CheckBox("Case-sensitive", false) { X = 23, Y = 8 };
        _chkRegex.Toggled += _ => UpdatePreview();
        _chkCase.Toggled  += _ => UpdatePreview();
        Add(new Label("(use | to separate pairs)") { X = 44, Y = 8 });

        // ── Case conversion ───────────────────────────────────────────────────
        //
        //  Case:  (o) No change  ( ) UPPER  ( ) lower  ( ) Title
        //
        Add(new Label("Case:") { X = 1, Y = 10, Width = 6 });
        _rgCase = new RadioGroup(
            new NStack.ustring[] { "No change", "UPPER", "lower", "Title" })
        {
            X = 7, Y = 10,
            DisplayMode = DisplayModeLayout.Horizontal,
        };
        _rgCase.SelectedItemChanged += _ => UpdatePreview();

        // ── Counter ───────────────────────────────────────────────────────────
        //
        //  Counter:  Start: [___]  Step: [___]  Digits: [___]
        //
        Add(new Label("Counter:") { X = 1,  Y = 12, Width = 9 });
        Add(new Label("Start:")   { X = 11, Y = 12, Width = 7 });
        _tfStart = new TextField("1") { X = 18, Y = 12, Width = 5 };
        _tfStart.TextChanged += _ => UpdatePreview();

        Add(new Label("Step:")    { X = 25, Y = 12, Width = 6 });
        _tfStep = new TextField("1") { X = 31, Y = 12, Width = 5 };
        _tfStep.TextChanged += _ => UpdatePreview();

        Add(new Label("Digits:")  { X = 38, Y = 12, Width = 8 });
        _tfDigits = new TextField("3") { X = 46, Y = 12, Width = 4 };
        _tfDigits.TextChanged += _ => UpdatePreview();

        // ── Preview list ──────────────────────────────────────────────────────
        Add(new Label("Preview (original → new):") { X = 1, Y = 14 });
        _preview = new ListView { X = 1, Y = 15, Width = Dim.Fill(1), Height = Dim.Fill(2) };

        // ── Buttons ───────────────────────────────────────────────────────────
        var btnRename = new Button("_Rename") { IsDefault = true };
        btnRename.Clicked += DoRename;
        var btnCancel = new Button("_Cancel");
        btnCancel.Clicked += () => Application.RequestStop();
        var btnHelp = new Button("_Help");
        btnHelp.Clicked += ShowHelp;
        AddButton(btnHelp);
        AddButton(btnCancel);
        AddButton(btnRename);

        Add(_tfMask, _tfExtMask,
            _tfSearch, _tfReplace, _chkRegex, _chkCase,
            _rgCase,
            _tfStart, _tfStep, _tfDigits,
            _preview);
    }

    // ── Mask / placeholder engine ─────────────────────────────────────────────

    /// <summary>
    /// Expands all <c>[…]</c> placeholders in <paramref name="mask"/> and
    /// returns the resulting string.
    /// <para>
    /// Inline case modifiers <c>[U]</c>, <c>[c]</c>, <c>[L]</c> convert the
    /// text produced by the immediately preceding placeholder.
    /// </para>
    /// </summary>
    private static string ApplyMask(
        string mask, string stem, string origExt, string parentName,
        int counterVal, int digits, DateTime modified, DateTime now)
    {
        var result = new System.Text.StringBuilder(mask.Length + 16);
        int lastPlaceholderStart = 0;
        int i = 0;

        while (i < mask.Length)
        {
            if (mask[i] == '[')
            {
                int close = mask.IndexOf(']', i + 1);
                if (close < 0) { result.Append(mask[i++]); continue; }

                string token = mask.Substring(i + 1, close - i - 1);

                // Inline case modifiers – applied to the preceding placeholder's output
                if (token is "c" or "L")
                {
                    ConvertRange(result, lastPlaceholderStart, upper: false);
                    lastPlaceholderStart = result.Length;
                    i = close + 1;
                    continue;
                }
                if (token == "U")
                {
                    ConvertRange(result, lastPlaceholderStart, upper: true);
                    lastPlaceholderStart = result.Length;
                    i = close + 1;
                    continue;
                }

                lastPlaceholderStart = result.Length;
                result.Append(ResolveToken(
                    token, stem, origExt, parentName,
                    counterVal, digits, modified, now));
                i = close + 1;
            }
            else
            {
                result.Append(mask[i++]);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Converts characters in <paramref name="sb"/> from index
    /// <paramref name="from"/> to the end to upper- or lower-case in place.
    /// </summary>
    private static void ConvertRange(System.Text.StringBuilder sb, int from, bool upper)
    {
        for (int k = from; k < sb.Length; k++)
            sb[k] = upper
                ? char.ToUpperInvariant(sb[k])
                : char.ToLowerInvariant(sb[k]);
    }

    private static string ResolveToken(
        string token, string stem, string origExt, string parentName,
        int counterVal, int digits, DateTime modified, DateTime now)
    {
        switch (token)
        {
            case "N": return stem;
            case "E": return origExt.TrimStart('.');
            case "P": return parentName;
            case "G": return Guid.NewGuid().ToString("N");          // 32 hex, no dashes
            case "T": return now.ToString("yyyyMMdd_HHmmss");
            case "Y": return modified.Year.ToString("D4");
            case "M": return modified.Month.ToString("D2");
            case "D": return modified.Day.ToString("D2");
            case "h": return modified.Hour.ToString("D2");
            case "m": return modified.Minute.ToString("D2");
            case "s": return modified.Second.ToString("D2");
        }

        // ── [C], [C:n], [C:A] ───────────────────────────────────────────────
        if (token == "C" || token.StartsWith("C:"))
        {
            string spec = token.Length > 2 ? token.Substring(2) : string.Empty;

            if (string.Equals(spec, "A", StringComparison.OrdinalIgnoreCase))
                return ToAlphaCounter(counterVal);

            int effectiveDigits = int.TryParse(spec, out int d) ? d : digits;
            return counterVal.ToString().PadLeft(effectiveDigits, '0');
        }

        // ── [N…] – name substrings ───────────────────────────────────────────
        if (token.Length > 1 && token[0] == 'N')
        {
            string range = token.Substring(1);

            // [N-n]: last n characters
            if (range.StartsWith("-") && range.Length > 1 &&
                int.TryParse(range.Substring(1), out int lastN) && lastN > 0)
            {
                int startIdx = Math.Max(0, stem.Length - lastN);
                return stem.Substring(startIdx);
            }

            // [Na-b]: characters a through b (1-indexed, inclusive)
            int dash = range.IndexOf('-');
            if (dash > 0 &&
                int.TryParse(range.Substring(0, dash),  out int from) &&
                int.TryParse(range.Substring(dash + 1), out int to))
            {
                from = Math.Max(1, from) - 1;   // to 0-based
                to   = Math.Min(to, stem.Length);
                return from < to ? stem.Substring(from, to - from) : string.Empty;
            }

            // [Nn]: single character at position n (1-indexed)
            if (int.TryParse(range, out int pos))
            {
                pos -= 1;
                return pos >= 0 && pos < stem.Length ? stem[pos].ToString() : string.Empty;
            }
        }

        return $"[{token}]";    // unrecognised – pass through unchanged
    }

    /// <summary>
    /// Converts a 1-based integer to an alphabetic counter
    /// (1→A, 26→Z, 27→AA, 28→AB, …).
    /// </summary>
    private static string ToAlphaCounter(int value)
    {
        if (value <= 0) value = 1;
        var stack = new System.Collections.Generic.Stack<char>();
        while (value > 0)
        {
            value--;
            stack.Push((char)('A' + value % 26));
            value /= 26;
        }
        return new string(stack.ToArray());
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    private void UpdatePreview()
    {
        int start  = int.TryParse(_tfStart.Text?.ToString(),  out int sv)  ? sv  : 1;
        int step   = int.TryParse(_tfStep.Text?.ToString(),   out int stv) ? stv : 1;
        int digits = int.TryParse(_tfDigits.Text?.ToString(), out int dv)  ? dv  : 3;
        var now    = DateTime.Now;

        var rows = _paths.Select((p, idx) =>
        {
            string orig  = Path.GetFileName(p);
            int    cval  = start + idx * step;
            string next  = ApplyRules(p, cval, digits, now);
            string arrow = orig == next ? "(unchanged)" : $"→ {next}";
            return $"{orig.PadRight(34)} {arrow}";
        }).ToList();

        _preview.SetSource(rows);
        Application.Refresh();
    }

    // ── Core rename logic ─────────────────────────────────────────────────────

    private string ApplyRules(string fullPath, int counterVal, int digits, DateTime now)
    {
        string name       = Path.GetFileName(fullPath);
        string origExt    = Path.GetExtension(name);
        string stem       = Path.GetFileNameWithoutExtension(name);
        string parentName = Path.GetFileName(
            Path.GetDirectoryName(fullPath) ?? string.Empty) ?? string.Empty;

        DateTime modified;
        try   { modified = File.GetLastWriteTime(fullPath); }
        catch { modified = now; }

        // ── 1. Expand rename mask → new stem ──────────────────────────────────
        string maskText = _tfMask.Text?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(maskText)) maskText = "[N]";
        string newStem = ApplyMask(maskText, stem, origExt, parentName,
                                   counterVal, digits, modified, now);

        // ── 2. Expand extension mask → new ext ────────────────────────────────
        string extMaskText = _tfExtMask.Text?.ToString() ?? string.Empty;
        string newExt;
        if (string.IsNullOrEmpty(extMaskText))
        {
            newExt = origExt;
        }
        else
        {
            string resolved = ApplyMask(extMaskText, stem, origExt, parentName,
                                        counterVal, digits, modified, now);
            newExt = string.IsNullOrEmpty(resolved)
                ? string.Empty
                : resolved.StartsWith('.') ? resolved : '.' + resolved;
        }

        // ── 3. Search / Replace (pipe-separated pairs) on the new stem ────────
        string searchText  = _tfSearch.Text?.ToString()  ?? string.Empty;
        string replaceText = _tfReplace.Text?.ToString() ?? string.Empty;

        if (!string.IsNullOrEmpty(searchText))
        {
            string[] searches = searchText.Split('|');
            string[] replaces = replaceText.Split('|');

            var opts = RegexOptions.CultureInvariant;
            if (!_chkCase.Checked) opts |= RegexOptions.IgnoreCase;

            for (int i = 0; i < searches.Length; i++)
            {
                string s = searches[i];
                if (string.IsNullOrEmpty(s)) continue;
                string r = i < replaces.Length ? replaces[i] : string.Empty;

                try
                {
                    string pattern = _chkRegex.Checked ? s : Regex.Escape(s);
                    newStem = Regex.Replace(newStem, pattern, r, opts);
                }
                catch { /* invalid pattern – skip this pair */ }
            }
        }

        // ── 4. Global case conversion ─────────────────────────────────────────
        newStem = _rgCase.SelectedItem switch
        {
            1 => newStem.ToUpperInvariant(),
            2 => newStem.ToLowerInvariant(),
            3 => CultureInfo.InvariantCulture.TextInfo
                    .ToTitleCase(newStem.ToLowerInvariant()),
            _ => newStem
        };

        return newStem + newExt;
    }

    // ── Commit ────────────────────────────────────────────────────────────────

    private void DoRename()
    {
        int start  = int.TryParse(_tfStart.Text?.ToString(),  out int sv)  ? sv  : 1;
        int step   = int.TryParse(_tfStep.Text?.ToString(),   out int stv) ? stv : 1;
        int digits = int.TryParse(_tfDigits.Text?.ToString(), out int dv)  ? dv  : 3;
        var now    = DateTime.Now;

        // First pass: count files that would change
        int changedCount = 0;
        for (int i = 0; i < _paths.Length; i++)
        {
            int cv = start + i * step;
            if (ApplyRules(_paths[i], cv, digits, now) != Path.GetFileName(_paths[i]))
                changedCount++;
        }

        if (changedCount == 0)
        {
            Tui.Message("Batch Rename", "No files would be changed with the current settings.");
            return;
        }

        if (!Tui.Confirm("Confirm Rename", $"Rename {changedCount} file(s)?"))
            return;

        var errors = new System.Text.StringBuilder();

        for (int i = 0; i < _paths.Length; i++)
        {
            int    cv      = start + i * step;
            string orig    = Path.GetFileName(_paths[i]);
            string newName = ApplyRules(_paths[i], cv, digits, now);
            if (newName == orig) continue;

            string newPath = Path.Combine(Path.GetDirectoryName(_paths[i])!, newName);
            try   { File.Move(_paths[i], newPath); }
            catch (Exception ex) { errors.AppendLine($"{orig}: {ex.Message}"); }
        }

        Application.RequestStop();

        if (errors.Length > 0)
            Console.Error.WriteLine($"Rename errors:\n{errors}");
    }

    // ── Help ──────────────────────────────────────────────────────────────────

    private static void ShowHelp()
    {
        var dlg = new Dialog("Batch Rename – Help", 76, 34);

        var tv = new TextView
        {
            X        = 1,
            Y        = 1,
            Width    = Dim.Fill(1),
            Height   = Dim.Fill(2),
            ReadOnly = true,
            Text     = HelpText,
        };

        var btnClose = new Button("_Close") { IsDefault = true };
        btnClose.Clicked += () => Application.RequestStop();
        dlg.AddButton(btnClose);
        dlg.Add(tv);

        Application.Run(dlg);
    }

    private const string HelpText =
        "MASK  (Rename mask & Extension field)\n" +
        "────────────────────────────────────────────────────────────────────\n" +
        " Type literal text freely; wrap dynamic parts in [ ] brackets.\n" +
        " Example:  Project_[C:3]_[Y][M][D]  →  Project_001_20260222\n" +
        "\n" +
        "NAME PLACEHOLDERS\n" +
        " [N]      Entire original filename (without extension)\n" +
        " [N1-5]   Characters 1–5 of the name  (1-indexed, inclusive)\n" +
        " [N-5]    Last 5 characters of the name\n" +
        " [P]      Immediate parent folder name\n" +
        "\n" +
        "COUNTER\n" +
        " [C]      Counter using global Start / Step / Digits values\n" +
        " [C:3]    Counter with inline digit width  →  001, 002, 003 …\n" +
        " [C:A]    Alphabetic counter  →  A, B … Z, AA, AB …\n" +
        " Start: first value   Step: increment   Digits: zero-pad width\n" +
        "\n" +
        "DATE & TIME  (uses file modification time)\n" +
        " [Y][M][D]   Year (4-digit), Month (2), Day (2)\n" +
        " [h][m][s]   Hour (2), Minute (2), Second (2)\n" +
        " [T]         Current timestamp when Rename is pressed\n" +
        "             (format: yyyyMMdd_HHmmss)\n" +
        "\n" +
        "OTHER\n" +
        " [E]   Original extension without the dot\n" +
        " [G]   Unique GUID per file (32 hex chars, no dashes)\n" +
        "\n" +
        "EXTENSION FIELD\n" +
        " Leave blank or use [E] to keep the original extension.\n" +
        " Type  txt  (no dot) to change all files to .txt.\n" +
        " The dot is added automatically — do not include it.\n" +
        "\n" +
        "INLINE CASE MODIFIERS\n" +
        " Place immediately after a placeholder to convert its output:\n" +
        " [U]     → UPPERCASE       [c] or [L]  → lowercase\n" +
        " Examples:\n" +
        "   [N][c]         →  lowercase name\n" +
        "   [P][U]_[N]     →  PARENTFOLDER_originalname\n" +
        "   [Y][M][D]_[N][c]  →  20260222_lowercase_name\n" +
        "\n" +
        "SEARCH & REPLACE\n" +
        " Applied to the expanded mask result (stem only, not extension).\n" +
        " Use  |  to define multiple substitutions in a single pass:\n" +
        "   Search:   ä|ö|ü       Replace:  ae|oe|ue\n" +
        " Enable RegEx for patterns and capture groups ($1, $2 …).\n" +
        "\n" +
        "CASE  (global dropdown)\n" +
        " Applied last, after the mask and search/replace.\n" +
        " For per-placeholder conversion use inline [U] / [c] instead.\n" +
        "\n" +
        "QUICK EXAMPLES\n" +
        " [Y]-[M]-[D]_[N]       →  2026-02-22_report\n" +
        " [P][c]_[C:3]_[N]      →  vacation_001_img\n" +
        " [N1-3][c]_[C:A]       →  rep_A  (3-char prefix + alpha counter)\n" +
        " [G]                    →  a3f8c1d2…  (collision-proof unique names)\n";
}
