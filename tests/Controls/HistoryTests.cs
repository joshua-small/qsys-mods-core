using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QsysMods.Core;

internal static partial class Program
{
    static void HistoryTests(Window main)
    {
        int undo = 0, redo = 0;
        bool ready = true;
        main.CommandBindings.Add(new(ApplicationCommands.Undo, (_, e) => { undo++; e.Handled = true; }, (_, e) => { e.CanExecute = ready; e.Handled = true; }));
        main.CommandBindings.Add(new(ApplicationCommands.Redo, (_, e) => { redo++; e.Handled = true; }, (_, e) => { e.CanExecute = ready; e.Handled = true; }));
        Commands.DiscoverNative(main);
        var child = new Window(); var button = new Button(); var text = new TextBox();
        var body = new StackPanel(); body.Children.Add(button); body.Children.Add(text); child.Content = body;
        var engine = ModControls.Bindings;
        var duplicateUndo = new KeyGesture(Key.Z, ModifierKeys.Control);
        ApplicationCommands.Undo.InputGestures.Add(duplicateUndo);
        var nativeUndoEntries = engine.DocumentHistoryEntries().Where(e => e.Command.Definition.Native == ApplicationCommands.Undo).ToArray();
        Check(nativeUndoEntries.Count(e => e.Sequence == ControlSequence.Parse("Ctrl+Z")) == 1,
            "Duplicate native Undo gestures resolve to one history shortcut");
        var duplicateState = new ControlChordState();
        Check(duplicateState.Feed(ControlSequence.Parse("Ctrl+Z").First,
            nativeUndoEntries.Select(e => (e.Command.Definition.Id, e.Sequence)), out _) == Commands.NativeId(ApplicationCommands.Undo),
            "Real host duplicate Undo gestures execute without an ambiguous-match exception");
        ApplicationCommands.Undo.InputGestures.Remove(duplicateUndo);
        engine.AttachDocumentHistory(child); engine.AttachDocumentHistory(child);
        Check(child.CommandBindings.Count == 2, "Repeated history-window registration does not duplicate forwarding");
        Check(ApplicationCommands.Undo.CanExecute(null, button), "Operation-window button routes Undo availability to owner");
        ApplicationCommands.Undo.Execute(null, button); ApplicationCommands.Redo.Execute(null, button);
        Check(undo == 1 && redo == 1, "Undo and Redo execute once on the document owner without changing focus");
        ready = false;
        Check(!ApplicationCommands.Undo.CanExecute(null, button), "Native unavailable history remains blocked");
        ApplicationCommands.Undo.Execute(null, button);
        Check(undo == 1, "Unavailable history cannot execute"); ready = true;
        main.IsEnabled = false;
        Check(!ApplicationCommands.Undo.CanExecute(null, button), "Disabled modal owner cannot receive document history"); main.IsEnabled = true;
        text.AppendText("local edit");
        ApplicationCommands.Undo.Execute(null, text);
        Check(undo == 1, "Text-field Undo never falls through into document Undo");
        using (var eligibility = new ControlHistoryWindow(main, new Window(), () => [], _ => { }))
        {
            Check(!eligibility.Eligible(text) && !eligibility.Eligible(new PasswordBox()) && !eligibility.Eligible(new ComboBox { IsEditable = true }),
                "History shortcut forwarding excludes all text-input families");
        }
        string id = Commands.NativeId(ApplicationCommands.Undo);
        engine.Apply(new(new() { [id] = "Ctrl+Alt+U > R" }));
        Check(engine.DocumentHistoryEntries().Where(e => e.Command.Definition.Id == id).Select(e => e.Sequence.Serialize()).SequenceEqual(["Ctrl+Alt+U > R"]),
            "Operation windows follow live custom history chords");
        Check(engine.DocumentHistoryEntries().All(e => ControlHistoryWindow.IsHistory(e.Command)), "History forwarding cannot claim other native/mod commands");
        var registered = (System.Collections.IDictionary)typeof(ControlBindings).GetField("historyWindows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(engine)!;
        var history = registered[child]!;
        var router = typeof(ControlHistoryWindow).GetField("keys", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(history)!;
        var pending = (ControlChordState)typeof(ControlChordRouter).GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(router)!;
        var sequence = ControlSequence.Parse("Ctrl+Alt+U > R");
        pending.Feed(sequence.First, [(id, sequence)], out _);
        Check(pending.Waiting, "Operation-window history chord can be pending before a remap");
        engine.Apply(new(new() { [id] = "" }));
        Check(!pending.Waiting, "Changing history keys cancels the old operation-window chord");
        Check(!engine.DocumentHistoryEntries().Any(e => e.Command.Definition.Id == id), "Cleared history assignment stays cleared in operation windows");
        child.Close();
        Check(child.CommandBindings.Count == 0, "Closed operation window removes forwarding bindings");
        engine.Apply(new(new()));
    }
}
