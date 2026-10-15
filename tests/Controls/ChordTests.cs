using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QsysMods.Core;

internal static partial class Program
{
    static void ChordTests(Window window)
    {
        var page = ControlSequence.Parse("Ctrl+Shift+N > P");
        var inventory = ControlSequence.Parse("Ctrl+Shift+N > Ctrl+Shift+I");
        var prefix = ControlSequence.Parse("Ctrl+Shift+N");
        page.Validate(); inventory.Validate();
        Check(page.IsChord && page.Second == new ControlStroke(Key.P, ModifierKeys.None), "Unmodified completion has a physical identity");
        Check(ControlSequence.Parse(page.Serialize()) == page, "Chord serialization is reversible");
        Check(ControlSequence.ParseList(page.Serialize() + "; Ctrl+F9; " + page.Serialize()).Length == 2, "Alternatives and duplicate sequences are distinct concepts");
        Check(page.Label == "Ctrl+Shift+N then P", "Menu label distinguishes sequence");
        Check(ControlSequence.Parse("Ctrl+OemComma").Label == "Ctrl+,", "Punctuation remains readable");
        Check(page.Conflicts(prefix) && prefix.Conflicts(page), "Prefix conflicts are symmetric");
        Check(!page.Conflicts(inventory), "Two distinct completions can share a prefix");
        Reject(() => ControlSequence.Parse("Ctrl+K > C > D"), "More than two strokes rejected");
        Reject(() => ControlSequence.Parse("Ctrl+K > Escape").Validate(), "Escape reserved");
        Reject(() => ControlSequence.Parse("I").Validate(), "Global ordinary letter rejected");
        var state = new ControlChordState();
        (string Id, ControlSequence Sequence)[] candidates = [("page", page), ("inventory", inventory)];
        Check(state.Feed(page.First, candidates, out bool consumed) is null && consumed && state.Waiting, "Prefix waits without running a command");
        Check(state.Feed(new(Key.LeftCtrl, ModifierKeys.Control), candidates, out consumed) is null && !consumed && state.Waiting, "Modifier transitions do not complete chords");
        Check(state.Feed(page.Second!.Value, candidates, out consumed) == "page" && consumed && !state.Waiting, "Correct completion dispatches once");
        Check(state.Feed(page.Second.Value, candidates, out consumed) is null && !consumed, "Completion alone is ordinary input");
        state.Feed(page.First, candidates, out _);
        Check(state.Feed(new(Key.X, ModifierKeys.None), candidates, out consumed) is null && consumed && !state.Waiting, "Invalid completion consumed without replay or execution");
        state.Feed(page.First, candidates, out _);
        Check(state.Feed(new(Key.Escape, ModifierKeys.None), candidates, out consumed) is null && consumed && !state.Waiting, "Escape cancels without execution");
        state.Feed(page.First, candidates, out _); state.Cancel();
        Check(!state.Waiting && state.Prefix is null, "Timeout/focus cancellation resets state");
        var transfer = SequenceChanges.Prepare(new() { ["absent"] = "Ctrl+F12" }, new Dictionary<string, string?> { ["new"] = page.Serialize() },
            new Dictionary<string, ControlSequence[]> { ["new"] = [page] }, [new("old", "Old", [prefix, ControlSequence.Parse("Ctrl+F10")])]);
        Check(transfer.Conflicts.Single().Contains("Old") && transfer.Overrides["old"] == "Ctrl+F10", "Prefix overwrite retains unrelated alternatives");
        Check(transfer.Overrides["absent"] == "Ctrl+F12", "Absent-mod binding survives chord overwrite");
        Reject(() => SequenceChanges.Prepare(new(), new Dictionary<string,string?>(),
            new Dictionary<string,ControlSequence[]> { ["a"]=[page], ["b"]=[prefix] }, []), "Batch rejects conflicting prefixes");
        Mods.InitializingId = "chord-fixture"; Mods.States["chord-fixture"] = "Ready";
        var native = new RoutedUICommand("Chord native", "Chord native", typeof(Program));
        var entry = Commands.Register(new(Commands.NativeId(native), "Chord native", "Q-SYS", () => {}, () => true, Native: native));
        Commands.RegisterDefaultBinding(native, "Ctrl+Alt+F8 > P", "Fixture", "Fixture context");
        var claimant = Commands.Register(new("chord-fixture/custom", "Personal key", "Fixture", () => {}, () => true));
        Mods.InitializingId = null;
        var custom = new Dictionary<string,string> { [claimant.Definition.Id] = "Ctrl+Alt+F8" };
        Check(ModControls.Bindings.Display(entry.Definition.Id, custom) == "", "New default yields to existing personal prefix");
        Check(custom.Count == 1 && custom[claimant.Definition.Id] == "Ctrl+Alt+F8", "Resolving defaults does not write personal preferences");
        var menu = new MenuItem { Header = "Chord native", Command = native };
        ((MenuItem)window.FindName("mainMenu_Tools")).Items.Add(menu);
        using (var engine = new ControlBindings(window))
        {
            engine.Apply(new(new()));
            Check(menu.InputGestureText == "Ctrl+Alt+F8 then P", "Native menu gets mod-declared chord");
            var context = new ContextMenu();
            var contextItem = new MenuItem { Header = "Context action", Command = native, InputGestureText = "original" };
            context.Items.Add(contextItem);
            engine.RefreshContextMenu(context);
            Check(contextItem.InputGestureText == "Ctrl+Alt+F8 then P", "Context menus display declared chords when opening");
            context.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
            Check(contextItem.InputGestureText == "original", "Closed context menus restore their previous hint without retained references");
            engine.Apply(new(new() { [entry.Definition.Id] = "Ctrl+F8 > X" }));
            Check(menu.InputGestureText == "Ctrl+F8 then X", "Native menu follows saved chord remap");
            engine.RefreshContextMenu(context);
            Check(contextItem.InputGestureText == "Ctrl+F8 then X", "Reopened context menu follows remapping");
            engine.Apply(new(new() { [entry.Definition.Id] = "Ctrl+F8 > Y" }));
            Check(contextItem.InputGestureText == "Ctrl+F8 then Y", "An open context menu follows live remapping");
            engine.Apply(new(new() { [entry.Definition.Id] = "" }));
            Check(menu.InputGestureText == "", "Clear removes chord hint");
            Check(contextItem.InputGestureText == "", "Clear removes the open context-menu hint");
            context.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
            Check(contextItem.InputGestureText == "original", "Live refresh preserves the original context hint for close");
            Reject(() => engine.Validate(new(new()) { FormatVersion = 3 }), "Future settings format rejected");
            Reject(() => engine.Validate(new(new() { [entry.Definition.Id] = "Ctrl+F8; Ctrl+F8 > X" })), "Own alternative cannot also be own prefix");
        }
        Check(menu.InputGestureText == "", "Disposal restores original menu hint");
        EscapeReleaseCancellation(window, claimant);
        PopupHints(window, native, entry.Definition.Id);
    }
    static void PopupHints(Window window, RoutedCommand native, string id)
    {
        var popup = new StackPanel();
        var button = new Button { Command = native, ToolTip = "Native action" };
        var richTip = new TextBlock { Text = "Rich tooltip" };
        var rich = new Button { Command = native, ToolTip = richTip };
        popup.Children.Add(button); popup.Children.Add(rich);
        using (var engine = new ControlBindings(window))
        {
            engine.Apply(new(new() { [id] = "Ctrl+F8 > G" })); engine.RefreshPopupToolbar(popup);
            Check((string)button.ToolTip == "Native action (Ctrl+F8 then G)", "Native icon tooltip shows effective chord");
            Check(ReferenceEquals(rich.ToolTip, richTip) && ReferenceEquals(button.Command, native), "Icon hints preserve rich tooltips and native command identity");
            engine.Apply(new(new() { [id] = "Ctrl+F8 > H" }));
            Check((string)button.ToolTip == "Native action (Ctrl+F8 then H)", "Open icon tooltip follows live remapping without duplicate labels");
            engine.Apply(new(new() { [id] = "" }));
            Check((string)button.ToolTip == "Native action", "Clear removes only the icon shortcut hint");
            engine.Apply(new(new()));
            Check((string)button.ToolTip == "Native action (Ctrl+Alt+F8 then P)", "Reset restores declared defaults on an open icon tooltip");
            popup.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Check((string)button.ToolTip == "Native action", "Closing native icon toolbar restores original tooltip");
            engine.RefreshPopupToolbar(popup);
            Check((string)button.ToolTip == "Native action (Ctrl+Alt+F8 then P)", "Reopened native icon toolbar resolves current keys once");
        }
        Check((string)button.ToolTip == "Native action", "Disposal restores an open native icon tooltip");
    }
    static void EscapeReleaseCancellation(Window window, RegisteredCommand command)
    {
        int runs = 0;
        var sequence = ControlSequence.Parse("F20 > G");
        using var router = new ControlChordRouter(window, () => [new(command, sequence)], (_, _) => runs++);
        var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle());
        bool Send(Key key, bool up = false)
        {
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) {
                RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent };
            window.RaiseEvent(e); return e.Handled;
        }
        Check(Send(Key.F20), "Escape-release fixture arms a chord through the window router"); Send(Key.F20, true);
        // Reproduce the observed missing key-down without assuming which
        // host or external input layer intercepted it.
        Check(Send(Key.Escape, true), "Escape release cancels when Escape down never reached the window");
        Check(!Send(Key.G) && runs == 0, "Completion after swallowed Escape down cannot execute"); Send(Key.G, true);
        Check(!Send(Key.Escape, true), "Unarmed Escape release remains native input");
        Send(Key.F20); Send(Key.F20, true); Send(Key.G); Send(Key.G, true);
        Check(runs == 1, "A fresh chord still executes exactly once after Escape release");
    }
}
