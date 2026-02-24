// UI/Tui.cs  –  thin wrapper around Terminal.Gui lifecycle

using Terminal.Gui;

namespace MCCompanion.UI;

/// <summary>
/// Helper to run a single Terminal.Gui dialog, then shut down cleanly.
/// All interactive commands use this so they don't need to manage TGui state.
/// </summary>
internal static class Tui
{
    /// <summary>
    /// Midnight Commander–style color scheme: white text on blue background,
    /// black on cyan for the focused/selected item, bright-yellow hotkeys.
    /// Apply to every Dialog so the whole suite looks consistent.
    /// </summary>
    internal static ColorScheme McColors => new()
    {
        Normal    = new Terminal.Gui.Attribute(Color.White,        Color.Blue),
        HotNormal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Blue),
        Focus     = new Terminal.Gui.Attribute(Color.Black,        Color.Cyan),
        HotFocus  = new Terminal.Gui.Attribute(Color.Black,        Color.BrightCyan),
        Disabled  = new Terminal.Gui.Attribute(Color.Gray,         Color.Blue),
    };

    /// <summary>
    /// Initialise Terminal.Gui, display the given Toplevel-derived view, then shutdown.
    /// The view is responsible for calling Application.RequestStop() to close itself.
    /// </summary>
    public static void Run(Func<Toplevel> factory)
    {
        Application.Init();
        try
        {
            var top = factory();
            Application.Run(top);
        }
        finally
        {
            Application.Shutdown();
        }
    }

    /// <summary>
    /// Show a simple message dialog and wait for the user to press OK or Escape.
    /// Safe to call inside an already-running Application.Run loop.
    /// </summary>
    public static void Message(string title, string message)
        => MessageBox.Query(60, 7, title, message, "OK");

    /// <summary>
    /// Show a yes/no confirmation.  Returns true if Yes was chosen.
    /// </summary>
    public static bool Confirm(string title, string question)
        => MessageBox.Query(60, 7, title, question, "Yes", "No") == 0;
}
