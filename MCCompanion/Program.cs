// MCCompanion – cross-platform Midnight Commander companion
// Runs on Windows and Linux (Ubuntu / Debian / Fedora / Arch).
// Uses Terminal.Gui v1 for all interactive dialogs (TUI, not GUI).
//
// Usage:  MCCompanion <verb> [args...]
//
// Verbs
//   clip-dir   <dir>               Copy directory path to clipboard
//   clip-path  <dir> <f1> [f2...]  Copy full path(s) to clipboard
//   clip-name  <dir> <f1> [f2...]  Copy filename(s) to clipboard
//   context    <path>              Shell context menu  (Windows) / open in file manager (Linux)
//   properties <path>              Properties dialog
//   open-with  <path>              Open-with dialog
//   checksum   <path>              MD5 / SHA-1 / SHA-256
//   rename     <dir> <f1> [f2...]  Batch rename
//   attributes <path>              View/change file attributes
//   dir-size   <dir>               Folder size
//   touch      <path>              Set file timestamp
//   terminal   <dir>               Open terminal here
//   compare     <path1> <path2>    Diff two files (launch external tool)
//   diff-view   <path1> <path2>    Diff two files (inline TUI viewer)
//   folder-size <dir> [items...]   Interactive folder size analyser

using MCCompanion.Commands;

// Terminal.Gui takes over the terminal for interactive commands;
// for fire-and-forget commands (clipboard, terminal launch) we skip TUI init.
if (args.Length == 0)
{
    ShowUsage();
    return;
}

string verb = args[0].ToLowerInvariant();
string[] rest = args[1..];

try
{
    ICommand cmd = verb switch
    {
        "clip-dir"   => new ClipboardCommand(ClipboardMode.Directory),
        "clip-path"  => new ClipboardCommand(ClipboardMode.Paths),
        "clip-name"  => new ClipboardCommand(ClipboardMode.Names),
        "context"    => new ContextCommand(),
        "properties" => new PropertiesCommand(),
        "open-with"  => new OpenWithCommand(),
        "checksum"   => new ChecksumCommand(),
        "rename"     => new BatchRenameCommand(),
        "attributes" => new AttributesCommand(),
        "dir-size"   => new DirSizeCommand(),
        "touch"      => new TouchCommand(),
        "terminal"   => new TerminalCommand(),
        "compare"     => new CompareCommand(),
        "diff-view"   => new DiffViewCommand(),
        "folder-size" => new FolderSizeCommand(),
        _            => new UnknownCommand(verb)
    };

    cmd.Execute(rest);
}
catch (Exception ex)
{
    // Ensure terminal is restored if TUI crashed mid-session
    try { Terminal.Gui.Application.Shutdown(); } catch { }
    Console.Error.WriteLine($"MCCompanion error: {ex.Message}");
    Environment.Exit(1);
}

static void ShowUsage()
{
    Console.WriteLine("""
        MCCompanion – Midnight Commander companion  (Windows + Linux)

        Usage:  MCCompanion <verb> [arguments]

        Verbs:
          clip-dir   <dir>              Copy directory path to clipboard
          clip-path  <dir> <f1> [f2...] Copy full path(s) to clipboard
          clip-name  <dir> <f1> [f2...] Copy filename(s) to clipboard
          context    <path>             Shell context menu / reveal in file manager
          properties <path>             File/folder properties
          open-with  <path>             Open with another application
          checksum   <path>             MD5 / SHA-1 / SHA-256
          rename     <dir> <f1> [f2...] Batch rename with live preview
          attributes <path>             View / change file attributes
          dir-size   <dir>              Calculate folder size
          touch      <path>             Set file timestamp
          terminal   <dir>              Open terminal in directory
          compare     <path1> <path2>   Diff two files (launch external tool)
          diff-view   <path1> <path2>   Diff two files (inline TUI viewer)
          folder-size <dir> [items...]  Interactive folder size analyser

        Integrate via Midnight Commander F2 user menu.
        See mc_menu_template.txt and README.md for details.
        """);
}
