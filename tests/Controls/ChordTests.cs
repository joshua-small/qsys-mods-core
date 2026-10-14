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
            context.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
            engine.Apply(new(new() { [entry.Definition.Id] = "" }));
            Check(menu.InputGestureText == "", "Clear removes chord hint");
            Reject(() => engine.Validate(new(new()) { FormatVersion = 3 }), "Future settings format rejected");
            Reject(() => engine.Validate(new(new() { [entry.Definition.Id] = "Ctrl+F8; Ctrl+F8 > X" })), "Own alternative cannot also be own prefix");
        }
        Check(menu.InputGestureText == "", "Disposal restores original menu hint");
    }
}
