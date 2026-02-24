// Commands/FolderSizeCommand.cs
// Interactive folder-size analyzer  (ncdu-style TUI, no external tools).
// Pass the MC current directory + tagged items via the F2 menu:
//   MCCompanion folder-size "%d" %t
// If nothing is tagged, the current directory is analysed.
// Sizes are calculated in the background and cached for the session
// (i.e., for the lifetime of this MCCompanion process).
//
// Navigation:
//   Enter / Space  = expand / collapse a directory
//   → / ←          = expand / collapse, or jump to parent
//   Insert         = mark / unmark item  (marks survive collapse)
//   Delete         = delete marked items (or item under cursor if nothing marked)
//   Click column   = sort by Name / Size / Date

using MCCompanion.UI;
using Terminal.Gui;

namespace MCCompanion.Commands;

// ── Command entry point ───────────────────────────────────────────────────────

internal class FolderSizeCommand : ICommand
{
    public void Execute(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: MCCompanion folder-size <dir> [items...]");
            return;
        }

        // BuildPaths: args[0]=dir (%d), args[1..]=names (%t) → full paths
        var paths = PathHelper.BuildPaths(args)
            .Where(p => Directory.Exists(p) || File.Exists(p))
            .Distinct()
            .ToArray();

        if (paths.Length == 0)
        {
            Console.Error.WriteLine($"Nothing to analyse. Path not found: {args[0]}");
            return;
        }

        Tui.Run(() => new FolderSizeDialog(paths));
    }
}

// ── Sort mode ─────────────────────────────────────────────────────────────────

internal enum SortMode { Size, Name, Date }

// ── Tree node ─────────────────────────────────────────────────────────────────

internal sealed class FolderNode
{
    // SizeBytes sentinel values
    public const long SizePending     = -1;  // calculation not yet started
    public const long SizeCalculating = -2;  // background task is running

    public FolderNode(string path, int depth)
    {
        Path  = path;
        Name  = System.IO.Path.GetFileName(path) is { Length: > 0 } n ? n : path;
        Depth = depth;

        bool isDir = Directory.Exists(path);
        IsFile = !isDir;
        try
        {
            LastWriteTime = isDir
                ? Directory.GetLastWriteTime(path)
                : File.GetLastWriteTime(path);
        }
        catch { LastWriteTime = DateTime.MinValue; }
    }

    public string   Path          { get; }
    public string   Name          { get; }
    public bool     IsFile        { get; }
    public int      Depth         { get; }
    public DateTime LastWriteTime { get; set; }
    public long     SizeBytes     { get; set; } = SizePending;
    public int      FileCount     { get; set; } = -1;
    public bool     IsExpanded    { get; set; }
    public bool  ChildrenLoaded   { get; set; }
    public List<FolderNode> Children { get; } = [];

    // Convenience properties for the SizeBytes state
    public bool IsReady       => SizeBytes >= 0;
    public bool IsPending     => SizeBytes == SizePending;
    public bool IsCalculating => SizeBytes == SizeCalculating;
}

// ── Dialog ────────────────────────────────────────────────────────────────────

internal sealed class FolderSizeDialog : Dialog
{
    // Session-level size cache.  Persists until MCCompanion exits.
    private static readonly Dictionary<string, (long Bytes, int Files)> _cache = new();

    private readonly List<FolderNode>    _roots;
    private readonly List<FolderNode>    _flat;      // visible flattened tree
    private readonly HashSet<FolderNode> _marked = new();
    private          SortMode            _sortMode  = SortMode.Size;
    private          ListView            _lv        = null!;
    private          Label               _lblStatus = null!;
    private          Button              _btnSortSize = null!;
    private          Button              _btnSortName = null!;
    private          Button              _btnSortDate = null!;
    private          int                 _pending;   // background tasks in flight (main-thread only)

    // ── Column widths ─────────────────────────────────────────────────────────
    // Row format:  " {size,10}  {files,6}  {indent}{icon}{name}"
    //              1    10     2    6     2        ...
    private const int  SizeW   = 10;
    private const int  FilesW  =  6;
    private const int  PrefixW = 1 + SizeW + 2 + FilesW + 2;  // = 21

    // Files at or above this size are treated as virtual/special and excluded
    // from folder totals.  /proc/kcore, for example, reports the full 64-bit
    // kernel address space: exactly 2^47 = 128 TiB on most Linux systems.
    private const long MaxSaneFileSize = 1L << 40;  // 1 TiB

    // ── Constructor ───────────────────────────────────────────────────────────

    public FolderSizeDialog(string[] paths)
        : base(" Folder Size Analysis ", Application.Driver.Cols, Application.Driver.Rows)
    {
        _roots = paths.Select(p => new FolderNode(p, 0)).ToList();
        _flat  = [.. _roots];

        Build();
        ExpandRootsAndStartCalculation();
    }

    // ── UI ────────────────────────────────────────────────────────────────────

    private void Build()
    {
        ColorScheme = Tui.McColors;

        // Y=1: Status line: mark count / calculating indicator / selected path
        _lblStatus = new Label("") { X = 1, Y = 1, Width = Dim.Fill(1) };
        Add(_lblStatus);

        // Y=2: Sort selector — clicking a button re-orders the visible tree
        Add(new Label("Sort:") { X = 1, Y = 2 });
        _btnSortSize = new Button("Size ▼") { X = 8,                          Y = 2 };
        _btnSortName = new Button("Name")   { X = Pos.Right(_btnSortSize) + 1, Y = 2 };
        _btnSortDate = new Button("Date")   { X = Pos.Right(_btnSortName) + 1, Y = 2 };
        _btnSortSize.Clicked += () => SetSort(SortMode.Size);
        _btnSortName.Clicked += () => SetSort(SortMode.Name);
        _btnSortDate.Clicked += () => SetSort(SortMode.Date);
        Add(_btnSortSize, _btnSortName, _btnSortDate);

        // Y=3: Column headers aligned with the render format
        Add(new Label($" {"SIZE",SizeW}  {"FILES",FilesW}  NAME") { X = 1, Y = 3 });

        // Y=4…: Scrollable tree list
        _lv = new ListView
        {
            X             = 1,
            Y             = 4,
            Width         = Dim.Fill(1),
            Height        = Dim.Fill(2),
            AllowsMarking = false,
        };
        _lv.Source              = new FolderSource(_flat, _marked);
        _lv.SelectedItemChanged += (_) => UpdateStatus();
        _lv.OpenSelectedItem    += (_) => ToggleExpand();   // Enter = expand / collapse
        _lv.KeyPress            += OnListKey;
        Add(_lv);

        // Bottom buttons
        var btnReload = new Button("_Reload");  btnReload.Clicked += OnReload;
        var btnDelete = new Button("_Delete");  btnDelete.Clicked += OnDelete;
        var btnClose  = new Button("_Close") { IsDefault = true };
        btnClose.Clicked += () => Application.RequestStop();

        AddButton(btnReload);
        AddButton(btnDelete);
        AddButton(btnClose);
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    private void OnListKey(KeyEventEventArgs e)
    {
        switch (e.KeyEvent.Key)
        {
            case Key.Space:
                ToggleExpand();        // Space = expand / collapse (same as Enter)
                e.Handled = true;
                break;
            case Key.InsertChar:
                ToggleMark();          // Insert = mark / unmark  (MC convention)
                e.Handled = true;
                break;
            case Key.CursorRight:
                ExpandSelected();
                e.Handled = true;
                break;
            case Key.CursorLeft:
                CollapseSelected();
                e.Handled = true;
                break;
            case Key.DeleteChar:
            case Key.Delete:
                OnDelete();
                e.Handled = true;
                break;
        }
    }

    // ── Marking ───────────────────────────────────────────────────────────────

    private void ToggleMark()
    {
        var node = SelectedNode();
        if (node == null) return;

        if (!_marked.Remove(node)) _marked.Add(node);

        // Advance to next item (MC behaviour: Insert moves down automatically)
        if (_lv.SelectedItem < _flat.Count - 1)
            _lv.SelectedItem++;

        RefreshList();
    }

    // ── Tree navigation ───────────────────────────────────────────────────────

    private FolderNode? SelectedNode()
    {
        int i = _lv.SelectedItem;
        return i >= 0 && i < _flat.Count ? _flat[i] : null;
    }

    private void ToggleExpand()
    {
        var n = SelectedNode();
        if (n == null || n.IsFile) return;
        if (n.IsExpanded) CollapseNode(n);
        else              ExpandNode(n);
    }

    private void ExpandSelected()
    {
        var n = SelectedNode();
        if (n is { IsFile: false, IsExpanded: false }) ExpandNode(n);
    }

    private void CollapseSelected()
    {
        var n = SelectedNode();
        if (n == null) return;
        if (!n.IsFile && n.IsExpanded)
            CollapseNode(n);
        else
        {
            // Jump to parent
            int idx = _lv.SelectedItem;
            for (int i = idx - 1; i >= 0; i--)
                if (_flat[i].Depth < n.Depth) { _lv.SelectedItem = i; break; }
        }
    }

    private void ExpandNode(FolderNode node)
    {
        int idx = _flat.IndexOf(node);
        if (idx < 0) return;

        node.IsExpanded = true;
        if (!node.ChildrenLoaded)
            LoadImmediateChildren(node);

        var children = OrderedChildren(node);
        _flat.InsertRange(idx + 1, children);

        RefreshList();

        // Start calculating any uncalculated children
        foreach (var child in children.Where(c => !c.IsFile && !c.IsReady))
            _ = BeginCalculateAsync(child);
    }

    private void CollapseNode(FolderNode node)
    {
        int idx = _flat.IndexOf(node);
        if (idx < 0) return;

        node.IsExpanded = false;

        // Remove all descendants from the visible list
        int end = idx + 1;
        while (end < _flat.Count && _flat[end].Depth > node.Depth) end++;
        _flat.RemoveRange(idx + 1, end - idx - 1);

        // Keep selected item in bounds
        if (_lv.SelectedItem > idx) _lv.SelectedItem = idx;

        RefreshList();
    }

    // Returns children in the order dictated by _sortMode.
    // Directories always come before files (MC panel convention).
    private IEnumerable<FolderNode> OrderedChildren(FolderNode parent) => _sortMode switch
    {
        SortMode.Name => parent.Children.Where(c => !c.IsFile).OrderBy(c => c.Name)
                         .Concat(parent.Children.Where(c => c.IsFile).OrderBy(c => c.Name)),

        SortMode.Date => parent.Children.Where(c => !c.IsFile).OrderByDescending(c => c.LastWriteTime)
                         .Concat(parent.Children.Where(c => c.IsFile).OrderByDescending(c => c.LastWriteTime)),

        _ =>            parent.Children.Where(c => !c.IsFile).OrderBy(c => c.Name)           // Size (default)
                         .Concat(parent.Children.Where(c => c.IsFile).OrderByDescending(c => c.SizeBytes)),
    };

    // ── Child enumeration ─────────────────────────────────────────────────────

    private void LoadImmediateChildren(FolderNode node)
    {
        node.ChildrenLoaded = true;
        try
        {
            var di = new DirectoryInfo(node.Path);

            foreach (var d in di.EnumerateDirectories().OrderBy(x => x.Name))
            {
                var child = new FolderNode(d.FullName, node.Depth + 1)
                    { LastWriteTime = d.LastWriteTime };  // use already-queried value
                if (_cache.TryGetValue(d.FullName, out var c))
                    (child.SizeBytes, child.FileCount) = c;
                node.Children.Add(child);
            }

            foreach (var f in di.EnumerateFiles().OrderByDescending(x => x.Length))
            {
                var child = new FolderNode(f.FullName, node.Depth + 1)
                    { SizeBytes = f.Length, FileCount = 1, LastWriteTime = f.LastWriteTime };
                _cache[f.FullName] = (f.Length, 1);
                node.Children.Add(child);
            }
        }
        catch { /* access denied – leave children empty */ }
    }

    // ── Background size calculation ───────────────────────────────────────────

    private async Task BeginCalculateAsync(FolderNode node)
    {
        if (node.IsFile || node.IsReady) return;

        if (_cache.TryGetValue(node.Path, out var cached))
        {
            node.SizeBytes = cached.Bytes;
            node.FileCount = cached.Files;
            Application.MainLoop?.Invoke(RefreshList);
            return;
        }

        _pending++;
        node.SizeBytes = FolderNode.SizeCalculating;

        long bytes = 0; int files = 0;
        try
        {
            (bytes, files) = await Task.Run(() =>
            {
                long b = 0; int f = 0;
                try
                {
                    foreach (var fi in new DirectoryInfo(node.Path)
                        .EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try { long l = fi.Length; if (l > 0 && l < MaxSaneFileSize) { b += l; f++; } } catch { }
                    }
                }
                catch { }
                return (b, f);
            }).ConfigureAwait(false);
        }
        catch { }

        Application.MainLoop?.Invoke(() =>
        {
            node.SizeBytes = bytes;
            node.FileCount = files;
            _cache[node.Path] = (bytes, files);
            _pending--;
            RefreshList();
        });
    }

    // ── Shared expand + calculation start (used by constructor and Reload) ────

    private void ExpandRootsAndStartCalculation()
    {
        foreach (var root in _roots)
        {
            if (!root.IsFile)
            {
                LoadImmediateChildren(root);
                root.IsExpanded = true;
                _flat.InsertRange(_flat.IndexOf(root) + 1, OrderedChildren(root));
            }
        }

        RefreshList();

        foreach (var node in _flat.ToList())
            if (!node.IsFile && !node.IsReady)
                _ = BeginCalculateAsync(node);
    }

    // ── Sort ──────────────────────────────────────────────────────────────────

    private void SetSort(SortMode mode)
    {
        _sortMode = mode;
        UpdateSortButtons();
        ApplySort();
    }

    private void UpdateSortButtons()
    {
        _btnSortSize.Text = _sortMode == SortMode.Size ? "Size ▼" : "Size";
        _btnSortName.Text = _sortMode == SortMode.Name ? "Name ▲" : "Name";
        _btnSortDate.Text = _sortMode == SortMode.Date ? "Date ▼" : "Date";
    }

    // Rebuild _flat from roots, re-expanding all previously expanded nodes
    // in the new sort order.  Expansion state and marks are preserved.
    private void ApplySort()
    {
        _flat.Clear();
        _flat.AddRange(_roots);

        // Forward pass: when we reach an expanded node, insert its children
        // right after it.  The children will then be encountered in turn and
        // their sub-trees inserted as well — no recursion needed.
        for (int i = 0; i < _flat.Count; i++)
        {
            var node = _flat[i];
            if (node.IsFile || !node.IsExpanded) continue;
            _flat.InsertRange(i + 1, OrderedChildren(node));
        }

        if (_lv.SelectedItem >= _flat.Count)
            _lv.SelectedItem = Math.Max(0, _flat.Count - 1);

        RefreshList();
    }

    // ── Reload ────────────────────────────────────────────────────────────────

    private void OnReload()
    {
        _cache.Clear();
        _marked.Clear();  // tree nodes get recreated; old node references become stale

        foreach (var root in _roots) ResetTree(root);
        _flat.Clear();
        _flat.AddRange(_roots);

        ExpandRootsAndStartCalculation();
    }

    private static void ResetTree(FolderNode node)
    {
        node.SizeBytes      = FolderNode.SizePending;
        node.FileCount      = -1;
        node.IsExpanded     = false;
        node.ChildrenLoaded = false;
        foreach (var c in node.Children) ResetTree(c);
        node.Children.Clear();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    private void OnDelete()
    {
        // Determine targets: all marked items visible in the flat list,
        // or just the item under the cursor if nothing is marked.
        List<FolderNode> targets = _marked.Count > 0
            ? _flat.Where(n => _marked.Contains(n)).ToList()
            : SelectedNode() is { } sel ? [sel] : [];

        if (targets.Count == 0) return;

        // When deleting multiple items, skip any that are descendants of another
        // target — deleting the parent recursively removes them anyway.
        List<FolderNode> rootTargets = targets.Count == 1 ? targets : targets
            .Where(n => !targets.Any(other =>
                other != n && n.Path.StartsWith(
                    other.Path + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Confirmation — "Cancel" is default (Enter never deletes by accident)
        string title   = rootTargets.Count == 1
            ? $"Delete {(rootTargets[0].IsFile ? "file" : "directory")}?"
            : $"Delete {rootTargets.Count} items?";
        string message = BuildDeleteConfirmMessage(rootTargets);

        if (MessageBox.Query(64, 12, title, message, "Cancel", "Delete permanently") != 1)
            return;

        // Delete each top-level target
        var errors = new List<string>();
        foreach (var node in rootTargets)
        {
            try
            {
                if (node.IsFile) File.Delete(node.Path);
                else             Directory.Delete(node.Path, recursive: true);
            }
            catch (Exception ex)
            {
                errors.Add($"{node.Name}: {ex.Message}");
                continue;
            }

            InvalidateAncestorSizes(node);
            _cache.Remove(node.Path);
            _marked.Remove(node);
            RemoveFromFlat(node);
            foreach (var root in _roots) RemoveChild(root, node);
        }

        if (errors.Count > 0)
            Tui.Message("Deletion errors", string.Join("\n", errors));

        if (_lv.SelectedItem >= _flat.Count)
            _lv.SelectedItem = Math.Max(0, _flat.Count - 1);

        RefreshList();
    }

    private static string BuildDeleteConfirmMessage(List<FolderNode> targets)
    {
        if (targets.Count == 1)
        {
            var n = targets[0];
            string details = n.IsFile
                ? $"File:  {n.Path}"
                : $"Directory:  {n.Path}";
            if (!n.IsFile && n.FileCount > 0) details += $"\n\nContains ~{n.FileCount:N0} file(s)";
            if (!n.IsFile && n.SizeBytes > 0) details += $"  ({FormatSize(n.SizeBytes).Trim()})";
            return $"{details}\n\nThis CANNOT be undone.";
        }

        long total     = targets.Where(n => n.IsReady && n.SizeBytes > 0).Sum(n => n.SizeBytes);
        int  fileCount = targets.Where(n => n.FileCount > 0).Sum(n => n.FileCount);
        string summary = $"{targets.Count} selected items";
        if (total > 0)     summary += $"  ~{FormatSize(total).Trim()}";
        if (fileCount > 0) summary += $"  ({fileCount:N0} file(s))";
        return $"{summary}\n\nThis CANNOT be undone.";
    }

    private void InvalidateAncestorSizes(FolderNode deleted)
    {
        foreach (var ancestor in _flat)
        {
            if (ancestor.Depth < deleted.Depth &&
                deleted.Path.StartsWith(ancestor.Path + System.IO.Path.DirectorySeparatorChar,
                                        StringComparison.OrdinalIgnoreCase))
            {
                if (ancestor.IsReady && deleted.IsReady)
                    ancestor.SizeBytes = Math.Max(0, ancestor.SizeBytes - deleted.SizeBytes);
                if (ancestor.FileCount >= 0 && deleted.FileCount >= 0)
                    ancestor.FileCount = Math.Max(0, ancestor.FileCount - deleted.FileCount);
                _cache.Remove(ancestor.Path);
            }
        }
    }

    private void RemoveFromFlat(FolderNode node)
    {
        int idx = _flat.IndexOf(node);
        if (idx < 0) return;
        int end = idx + 1;
        while (end < _flat.Count && _flat[end].Depth > node.Depth) end++;
        _flat.RemoveRange(idx, end - idx);
    }

    private static void RemoveChild(FolderNode parent, FolderNode target)
    {
        if (parent.Children.Remove(target)) return;
        foreach (var c in parent.Children) RemoveChild(c, target);
    }

    // ── UI refresh ────────────────────────────────────────────────────────────

    private void RefreshList()
    {
        _lv.Source = new FolderSource(_flat, _marked);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var node  = SelectedNode();
        var parts = new List<string>();

        if (_marked.Count > 0)
        {
            long markedSize = _marked.Where(n => n.IsReady && n.SizeBytes > 0)
                                     .Sum(n => n.SizeBytes);
            string sizeInfo = markedSize > 0 ? $"  {FormatSize(markedSize).Trim()}" : "";
            parts.Add($"[{_marked.Count} marked{sizeInfo}]");
        }

        if (_pending > 0)
            parts.Add($"[Calculating {_pending}…]");

        // When sorted by date, show the selected item's modification time
        if (_sortMode == SortMode.Date && node != null && node.LastWriteTime != DateTime.MinValue)
            parts.Add(node.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));

        parts.Add(node?.Path ?? "");

        _lblStatus.Text = string.Join("  ", parts);
        Application.Refresh();
    }

    // ── Formatting helpers ────────────────────────────────────────────────────

    private static string FormatSize(long bytes)
    {
        if (bytes == 0) return "0 B".PadLeft(SizeW);
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        string s = i == 0 ? $"{(int)v} B" : $"{v:F2} {u[i]}";
        return s.PadLeft(SizeW);
    }

    private static string FormatFiles(int count)
    {
        if (count < 0) return new string(' ', FilesW);
        if (count >= 1_000_000) return $"{count / 1_000_000}M".PadLeft(FilesW);
        if (count >= 10_000)    return $"{count / 1_000}K".PadLeft(FilesW);
        return $"{count,6:N0}";
    }

    // ── IListDataSource – colored per-row rendering ───────────────────────────
    // Nested so it can share the private column constants and format helpers
    // of FolderSizeDialog without any public/internal surface area.

    private sealed class FolderSource : IListDataSource
    {
        private readonly List<FolderNode>    _flat;
        private readonly HashSet<FolderNode> _marked;

        // Size thresholds for colour coding
        private const long BytesPerGb = 1L   * 1024 * 1024 * 1024;
        private const long Bytes100Mb = 100L * 1024 * 1024;
        private const long Bytes10Mb  = 10L  * 1024 * 1024;

        public FolderSource(List<FolderNode> flat, HashSet<FolderNode> marked)
        {
            _flat   = flat;
            _marked = marked;
        }

        public int  Count                         => _flat.Count;
        public int  Length                        => 0;   // no horizontal scroll
        public bool IsMarked(int item)            => false;
        public void SetMark(int item, bool value) { }
        public System.Collections.IList ToList()  => _flat;

        public void Render(ListView container, ConsoleDriver driver,
                           bool marked, int item, int col, int line, int width, int start = 0)
        {
            if (item < 0 || item >= _flat.Count) return;

            var  node     = _flat[item];
            var  norm     = container.ColorScheme.Normal;  // White on Blue from our scheme
            bool isCursor = item == container.SelectedItem;
            bool isMarked = _marked.Contains(node);

            // Size column (right-aligned, 10 chars)
            string sizeStr = node.IsCalculating ? "       … "
                           : !node.IsReady      ? new string(' ', SizeW)
                           :                      FormatSize(node.SizeBytes);

            // Files column (right-aligned, 6 chars)
            string fileStr = FormatFiles(node.FileCount);

            // Expand/collapse icon + depth indent
            string indent = new string(' ', node.Depth * 2);
            string icon   = node.IsFile     ? "  "
                          : node.IsExpanded ? "▼ "
                          :                   "▶ ";

            // Name truncated to available space
            int nameW = Math.Max(1, width - PrefixW - indent.Length - icon.Length);
            string name = node.Name.Length <= nameW ? node.Name : node.Name[..nameW];

            // Assemble full row string
            string row = $" {sizeStr}  {fileStr}  {indent}{icon}{name}";
            if (row.Length > width) row = row[..width];
            if (row.Length < width) row = row.PadRight(width);

            // ── Colour priority (highest first) ──────────────────────────────
            // Cursor+marked: inverted marked colour  (black on bright yellow)
            // Cursor:        MC-style               (black on cyan)
            // Marked:        MC tagged style        (bright yellow on black)
            // Calculating:   activity indicator     (bright cyan on blue)
            // ≥ 1 GB:        red warning            (bright red on blue)
            // ≥ 100 MB:      caution                (bright yellow on blue)
            // Small file:    de-emphasised          (gray on blue)
            // Normal:        panel default          (white on blue)
            var attr = isCursor && isMarked
                ? new Terminal.Gui.Attribute(Color.Black,        Color.BrightYellow)
                : isCursor
                ? new Terminal.Gui.Attribute(Color.Black,        Color.Cyan)
                : isMarked
                ? new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black)
                : node.IsCalculating
                ? new Terminal.Gui.Attribute(Color.BrightCyan,   Color.Blue)
                : node.SizeBytes >= BytesPerGb
                ? new Terminal.Gui.Attribute(Color.BrightRed,    Color.Blue)
                : node.SizeBytes >= Bytes100Mb
                ? new Terminal.Gui.Attribute(Color.BrightYellow, Color.Blue)
                : node.IsFile && node.SizeBytes < Bytes10Mb
                ? new Terminal.Gui.Attribute(Color.Gray,         Color.Blue)
                : norm;

            // Apply horizontal-scroll offset and draw the row
            int skip = Math.Min(col, row.Length);
            string vis = row[skip..];
            if (vis.Length > width) vis = vis[..width];
            int pad = width - vis.Length;

            driver.SetAttribute(attr);
            driver.AddStr(vis);
            // Extend the highlight colour across the full row width so that the
            // cursor and marked backgrounds fill the entire row (MC behaviour).
            if (pad > 0)
            {
                driver.SetAttribute(isCursor || isMarked ? attr : norm);
                driver.AddStr(new string(' ', pad));
            }
        }
    }
}
