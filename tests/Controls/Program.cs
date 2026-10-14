using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QsysMods.Core;

internal static class Program
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static void Reject(Action action, string message) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception(message); }
    [STAThread]
    static void Main()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qsys-controls-tests-" + Guid.NewGuid().ToString("N"), "preferences.json");
        Preferences.PathName = path;
        var window = new Window();
        NameScope.SetNameScope(window, new NameScope());
        var root = new Grid(); var menu = new Menu(); var tools = new MenuItem { Header = "Tools" }; menu.Items.Add(tools);
        var canvas = new Canvas(); root.Children.Add(menu); root.Children.Add(canvas); window.Content = root;
        window.RegisterName("mainMenu", menu); window.RegisterName("mainMenu_Tools", tools);
        var native = new RoutedUICommand("Pack Left", "PackLeft", typeof(Program), new InputGestureCollection { new KeyGesture(Key.L, ModifierKeys.Control | ModifierKeys.Shift) });
        int nativeRuns = 0, packRuns = 0, errors = 0;
        window.CommandBindings.Add(new CommandBinding(native, (_, _) => nativeRuns++, (_, e) => e.CanExecute = true));
        Commands.Initialize(window); Commands.DiscoverNative(window); ModControls.Initialize(window);
        string id = Commands.NativeId(native);
        var legacy = new SettingsStore(path);
        legacy.Save(new Dictionary<string, object> { ["better-shortcuts"] = new BindingSettings(new() { [id] = "Ctrl+Alt+L", ["missing/action"] = "Ctrl+Alt+F12" }, new() { [id] = "Ctrl+Alt+L" }), ["other-mod"] = new { Keep = 42 } });
        Mods.InitializingId = "packing";
        var packing = new ControlProposal("packing/left", "packing", "Better Packing", "1", id, "Pack Left", "Schematic; valid selection", Scope: ControlScope.Schematic);
        ReplacementAvailability focus = ReplacementAvailability.Ready;
        bool fail = false;
        ModControls.RegisterReplacement(packing, native, () => new(focus, canvas), target => { if (fail) throw new Exception("Fixture rejection"); Check(ReferenceEquals(target, canvas), "Correct context target"); packRuns++; }, true);
        Mods.InitializingId = "second";
        var second = packing with { Id = "second/left", ProviderId = "second", ProviderName = "Second provider" };
        ModControls.RegisterReplacement(second, native, () => new(focus, canvas), _ => packRuns += 10);
        Mods.InitializingId = null; Mods.States["packing"] = "Ready"; Mods.States["second"] = "Ready";
        ModControls.CompleteInitialization();
        var state = ModControls.Current;
        Check(ControlPlans.Accepted(state, packing), "Legacy replacement setting preserved");
        Check(!ControlPlans.Reviewed(state, second), "New install requires review");
        Check(state.Bindings.Overrides[id] == "Ctrl+Alt+L" && state.Bindings.EditorOverrides![id] == "Ctrl+Alt+L", "Legacy keys and editor consent migrate together");
        Check(!new SettingsStore(path).Contains(ModControls.SettingsId), "Loading migration does not write settings");
        Check(ModControls.Bindings.Display(id, state.Bindings.Overrides) == "Ctrl+Alt+L", "Actual remap displayed");
        Check(ModControls.Bindings.Display(id, new()) == "Ctrl+Shift+L", "Original default retained after override");
        var entry = Commands.All.Single(c => c.Definition.Id == id);
        var info = ModControls.Describe(entry, state);
        Check(info.Source.Contains("Better Packing") && info.Source.Contains("Q-SYS (other contexts)"), "Scoped provider source");
        Check(info.Details.Contains("Awaiting review") && info.When.Contains("valid selection"), "Availability and context distinct");
        var change = ModControls.Prepare(state, [second.Id], [second.Id]);
        Check(change.Conflicts.Single().Contains("Better Packing"), "Competing provider requires visible overwrite");
        Check(ControlPlans.Accepted(state, packing) && !ControlPlans.Accepted(state, second), "Canceled provider preview leaves original intact");
        Check(!ControlPlans.Accepted(change.Settings, packing) && ControlPlans.Accepted(change.Settings, second), "Approved transfer selects one provider");
        Reject(() => ModControls.Prepare(state, [packing.Id, second.Id], [packing.Id, second.Id]), "Two overlapping providers must not silently win by order");
        ModControls.Publish(change.Settings); native.Execute(null, canvas);
        Check(packRuns == 10 && nativeRuns == 0, "Chosen provider executes exactly once");
        Mods.States["second"] = "Initialization failed";
        native.Execute(null, canvas);
        Check(nativeRuns == 1 && packRuns == 10, "Failed provider restores native route");
        Check(ModControls.Describe(entry, change.Settings).Source == "Q-SYS", "Failed provider is not reported active");
        Mods.States["second"] = "Not loaded";
        Check(ModControls.Describe(entry, change.Settings).Details.Contains("Not loaded"), "Absent provider distinguished from disabled replacement");
        Mods.States["second"] = "Ready";
        ModControls.Publish(state);
        native.Execute(null, canvas); Check(packRuns == 11 && nativeRuns == 1, "Canonical native action uses replacement with remapped keys");
        focus = ReplacementAvailability.Blocked;
        Check(!native.CanExecute(null, canvas), "Invalid selection blocked"); native.Execute(null, canvas);
        Check(packRuns == 11 && nativeRuns == 1, "Blocked action does not mutate either provider");
        focus = ReplacementAvailability.OutsideScope; native.Execute(null, canvas);
        Check(nativeRuns == 2 && packRuns == 11, "Outside scope native passthrough");
        focus = ReplacementAvailability.Ready; fail = true; native.Execute(null, canvas);
        Check(nativeRuns == 2 && packRuns == 11, "Replacement failure never falls through"); fail = false;
        var disabled = state.Copy(); disabled.Decisions[packing.Id] = new("1", false); ModControls.Publish(disabled); native.Execute(null, canvas);
        Check(nativeRuns == 3 && ModControls.Bindings.Display(id, disabled.Bindings.Overrides) == "Ctrl+Alt+L", "Disabling replacement keeps user keys");
        Check(!ControlPlans.Reviewed(state, packing with { Revision = "2" }), "New declaration revision requires review");
        Check(ControlPlans.Reviewed(state, packing with { ProviderName = "Renamed label" }), "Display-only changes do not reset a decision");
        Check(!ControlPlans.Overlaps(packing, second with { Scope = ControlScope.Uci }), "Known disjoint surfaces coexist");
        Check(ControlPlans.Overlaps(packing, second with { Scope = ControlScope.Any }), "Unknown/global surface overlaps schematic");
        var draft = new ControlDraft(state); draft.Stage(change.Settings);
        Check(ControlPlans.Accepted(state, packing), "Shared page draft does not mutate live state");
        var store = new SettingsStore(path); store.Save(new Dictionary<string, object> { [ModControls.SettingsId] = change.Settings });
        var reopened = ModControls.Load(new SettingsStore(path));
        Check(ControlPlans.Accepted(reopened, second) && !ControlPlans.Accepted(reopened, packing), "Replacement choice persists and migration cannot re-enable old choice");
        Check(new SettingsStore(path).Read("other-mod", () => new Dictionary<string, int>())["Keep"] == 42, "Unknown mod settings preserved");
        var stale = new SettingsStore(path); store.Save(new Dictionary<string, object> { [ModControls.SettingsId] = state });
        try { stale.Save(new Dictionary<string, object> { [ModControls.SettingsId] = change.Settings }); throw new Exception("Expected stale save refusal"); } catch (System.IO.IOException) { checks++; }

        // Real WPF routing on an unshown tree tests exception guards and disposal independently.
        var isolated = new Grid(); var testCommand = new RoutedCommand("Test", typeof(Program));
        isolated.CommandBindings.Add(new CommandBinding(testCommand, (_, _) => nativeRuns++, (_, e) => e.CanExecute = true));
        var replacement = new CommandReplacement(packing, testCommand, () => throw new Exception("Read failed"), _ => packRuns++);
        using (var router = new ReplacementRouter(isolated, () => [replacement], _ => errors++))
        { Check(!testCommand.CanExecute(null, isolated), "Read failure is closed"); testCommand.Execute(null, isolated); Check(errors == 1, "Execution error reported once"); }
        int before = nativeRuns; testCommand.Execute(null, isolated); Check(nativeRuns == before + 1, "Router disposal restores native");
        EngineAndReviewTests(window, canvas, id);
        PlanTests(); MigratedTransferTests();
        Console.WriteLine($"Passed {checks} shared controls checks. No Designer or visible window was started.");
    }
    static void EngineAndReviewTests(Window window, Canvas canvas, string nativeId)
    {
        Mods.InitializingId = "addon"; Mods.States["addon"] = "Ready";
        var added = Commands.Register(new("addon/action", "New action", "Addon", () => { }, () => true));
        var proposal = new ControlProposal("addon/key", "addon", "Addon", "1", added.Definition.Id, "New action shortcut", "Designer", "Ctrl+Alt+L");
        ModControls.RegisterShortcut(proposal); Mods.InitializingId = null;
        var state = ModControls.Current;
        var draft = new ControlDraft(state);
        var view = ControlReview.Create(draft, [proposal.Id]);
        IEnumerable<DependencyObject> Walk(DependencyObject node)
        {
            yield return node;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (var descendant in Walk(child)) yield return descendant;
        }
        var apply = Walk(view).OfType<Button>().Single(b => Equals(b.Content, "Apply selected"));
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Equals(apply.Content, "Overwrite and apply") && !draft.Value.Bindings.Overrides.ContainsKey(added.Definition.Id), "Review warns before staging bulk overwrite");
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(draft.Value.Bindings.Overrides[added.Definition.Id] == "Ctrl+Alt+L" && draft.Value.Bindings.Overrides[nativeId] == "", "Confirmed bulk review transfers only the selected key");
        Check(ModControls.Current.Bindings.Overrides[nativeId] == "Ctrl+Alt+L", "Review staging cannot change live keys before Preferences OK");
        var editor = new RoutedCommand("Direct editor", typeof(Program));
        var input = new KeyBinding(editor, new KeyGesture(Key.J, ModifierKeys.Control)); window.InputBindings.Add(input);
        var transfer = ModControls.Bindings.Prepare(added.Definition.Id, "Ctrl+J", state.Bindings.Overrides, state.Bindings.EditorOverrides);
        Check(transfer.Conflicts.Any(c => c.Contains("Direct editor")) && transfer.EditorOverrides[added.Definition.Id] == "Ctrl+J", "Direct editor conflicts are visible and consent is scoped");
        var changed = state with { Bindings = new(transfer.Overrides, transfer.EditorOverrides) };
        ModControls.Publish(changed); Check(!window.InputBindings.Contains(input), "Approved editor binding suppressed");
        ModControls.Publish(state); Check(window.InputBindings.Contains(input), "Removing override restores editor binding");
        var invalid = state.Copy(); invalid.Bindings.Overrides[added.Definition.Id] = "Ctrl+Alt+L";
        Reject(() => ModControls.Publish(invalid), "Unconfirmed collision rejected at save boundary");
        Check(ModControls.Current.Bindings.Overrides[nativeId] == "Ctrl+Alt+L" && window.InputBindings.Contains(input), "Validation failure preserves current live bindings");
        var failedDefault = Commands.Register(new("addon/default", "Default action", "Addon", () => { }, () => true, new(Key.F10, ModifierKeys.Control)));
        // Default command registration from outside initialization remains supported.
        Check(window.InputBindings.OfType<KeyBinding>().Any(b => b.Command == failedDefault.Command), "Core installs registered defaults without Better Shortcuts");
    }
    static BindingChange SinglePlan(Dictionary<string,string> draft, string id, string? value, KeyGesture[] desired, IEnumerable<BindingAssignment> assignments, Dictionary<string,string>? editors = null) =>
        BindingChanges.Prepare(draft, new Dictionary<string,string?> { [id] = value }, new Dictionary<string,KeyGesture[]> { [id] = desired }, assignments, editors);
    static void MigratedTransferTests()
    {
        var control = ModifierKeys.Control;
        var zero = new KeyGesture(Key.D0, control); var keypad = new KeyGesture(Key.NumPad0, control);
var draft = new Dictionary<string, string> { ["absent/mod"] = "Ctrl+Alt+F12", ["target"] = "Ctrl+D" };
var assignments = new[] {
    new BindingAssignment("target", "Target", [new(Key.D, control)]),
    new BindingAssignment("copy", "Copy", [new(Key.C, control), new(Key.Insert, control)]),
    new BindingAssignment("copy2", "Copy elsewhere", [new(Key.C, control)]),
    new BindingAssignment("zoom", "Zoom", [zero, keypad])
};
var transfer = SinglePlan(draft, "target", "Ctrl+C", [new(Key.C, control)], assignments);
Check(transfer.Conflicts.Length == 2 && transfer.Conflicts.Any(c => c.StartsWith("Copy elsewhere")), "Warn about every affected command, not only the first collision.");
Check(transfer.Overrides["target"] == "Ctrl+C" && transfer.Overrides["copy2"] == "", "Overwrite transfers the gesture and explicitly disables commands with no remaining bindings.");
Check(transfer.Overrides["copy"] == "Ctrl+Insert", "Overwrite must retain non-conflicting alternatives.");
Check(draft.Count == 2 && draft["target"] == "Ctrl+D" && !draft.ContainsKey("copy"), "Preview or canceled overwrite must not mutate the draft.");
Check(transfer.Overrides["absent/mod"] == "Ctrl+Alt+F12", "Overwrite must preserve temporarily absent commands.");
var keypadTransfer = SinglePlan(draft, "target", "Ctrl+0", [zero], assignments);
Check(keypadTransfer.Overrides["zoom"] == "Ctrl+NumPad0", "Number-row conflicts must not remove the distinct keypad binding.");
var reset = SinglePlan(draft, "target", null, [new(Key.C, control), new(Key.Insert, control)], assignments);
Check(!reset.Overrides.ContainsKey("target") && reset.Overrides["copy"] == "" && reset.Conflicts.Length == 2, "A conflicting reset must preview transfer of all restored defaults.");
var clear = SinglePlan(draft, "target", "", [], assignments);
Check(clear.Conflicts.Length == 0 && clear.Overrides.Count == draft.Count, "Clear must not change another command.");
var same = SinglePlan(draft, "target", "Ctrl+D", [new(Key.D, control)], assignments);
Check(same.Conflicts.Length == 0, "A command must not conflict with itself.");
Check(ControlGestures.Join(ControlGestures.ParseList("Ctrl+S; Ctrl+Insert; Ctrl+S")) == "Ctrl+S; Ctrl+Insert", "Stored alternative gestures must round-trip and deduplicate.");
Check(ControlGestures.ParseList("Ctrl+S").Single().Key == Key.S && ControlGestures.ParseList("").Length == 0, "Existing single-value and cleared preferences remain compatible.");
var retained = SinglePlan(draft, "target", "Ctrl+C", [new(Key.C, control)],
    [new("native", "Native action", [new(Key.C, control), new(Key.Delete), new(Key.F7)])]);
Check(ControlGestures.ParseList(retained.Overrides["native"]).Select(g => g.Key).SequenceEqual(new[] { Key.Delete, Key.F7 }), "Transfer must preserve native ordinary-key and function-key alternatives.");
var editorClaims = new Dictionary<string, string> { ["copy"] = "Ctrl+C; Ctrl+Insert", ["target"] = "Ctrl+D", ["absent/editor"] = "Ctrl+H" };
var editorTransfer = SinglePlan(draft, "target", "Ctrl+C", [new(Key.C, control)], assignments, editorClaims);
Check(editorTransfer.EditorOverrides["copy"] == "Ctrl+Insert", "Editor overwrite ownership must retain only surviving alternatives.");
Check(!editorTransfer.EditorOverrides.ContainsKey("target") && editorTransfer.EditorOverrides["absent/editor"] == "Ctrl+H", "Target changes must re-evaluate editor ownership while preserving absent owners.");
Check(editorClaims["target"] == "Ctrl+D" && editorClaims["copy"] == "Ctrl+C; Ctrl+Insert", "Cancel must preserve editor overwrite ownership too.");
var oldSettings = System.Text.Json.JsonSerializer.Deserialize<BindingSettings>("{\"Overrides\":{\"target\":\"Ctrl+D\"}}")!;
Check(oldSettings.Overrides["target"] == "Ctrl+D" && oldSettings.EditorOverrides is null, "Old preferences must load without inventing editor overwrite consent.");
var newSettings = new BindingSettings(transfer.Overrides, new() { ["target"] = "Ctrl+C" });
var reopened = System.Text.Json.JsonSerializer.Deserialize<BindingSettings>(System.Text.Json.JsonSerializer.Serialize(newSettings))!;
Check(reopened.Overrides["copy"] == "Ctrl+Insert" && reopened.EditorOverrides!["target"] == "Ctrl+C", "Saved conflict transfer and explicit editor ownership must survive reopen.");

    }
    static void PlanTests()
    {
        var ctrl = ModifierKeys.Control;
        var assignments = new BindingAssignment[] { new("a", "A", [new(Key.A, ctrl)]), new("b", "B", [new(Key.B, ctrl)]), new("copy", "Copy", [new(Key.C, ctrl), new(Key.Insert, ctrl)]) };
        var original = new Dictionary<string, string> { ["absent"] = "Ctrl+Alt+F12" };
        BindingChange Plan(Dictionary<string, string?> requests, Dictionary<string, string>? editors = null) => BindingChanges.Prepare(original, requests,
            requests.ToDictionary(p => p.Key, p => ControlGestures.ParseList(p.Value ?? "")), assignments, editors);
        var swap = Plan(new() { ["a"] = "Ctrl+B", ["b"] = "Ctrl+A" });
        Check(swap.Overrides["a"] == "Ctrl+B" && swap.Overrides["b"] == "Ctrl+A", "Bulk swaps keep both final bindings");
        var reversed = Plan(new() { ["b"] = "Ctrl+A", ["a"] = "Ctrl+B" });
        Check(reversed.Overrides.OrderBy(p => p.Key).SequenceEqual(swap.Overrides.OrderBy(p => p.Key)), "Bulk order independent");
        var transfer = Plan(new() { ["a"] = "Ctrl+C" }, new() { ["copy"] = "Ctrl+C; Ctrl+Insert" });
        Check(transfer.Conflicts.Single().StartsWith("Copy") && transfer.Overrides["copy"] == "Ctrl+Insert", "Overwrite names affected action and retains alternatives");
        Check(transfer.EditorOverrides["copy"] == "Ctrl+Insert", "Editor consent retains only surviving alternatives");
        Check(original.Count == 1 && transfer.Overrides["absent"] == original["absent"], "Preview and absent assignments preserved");
        Reject(() => Plan(new() { ["a"] = "Ctrl+C", ["b"] = "Ctrl+C" }), "Batch internal collision must require a choice");
        var clear = Plan(new() { ["a"] = "" }); Check(clear.Conflicts.Length == 0 && clear.Overrides["a"] == "", "Bulk clear does not displace others");
        Check(ControlGestures.Join([new(Key.D0, ctrl, "Ctrl+0"), new(Key.NumPad0, ctrl, "Ctrl+0"), new(Key.D0, ctrl)]) == "Ctrl+0; Ctrl+NumPad0", "Unique physical keys with no display alias duplication");
    }
}
