using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QsysMods.Core;

internal static partial class Program
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
        Check(!ModControls.HasReviewControls, "Empty general control review is hidden");
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
        PlanTests(); MigratedTransferTests(); GroupTests(window, canvas);
        AliasTests(native);
        MenuHintTests(window);
        ChordTests(window);
        EditorTests();
        DefaultConflictTests(window);
        HistoryTests(window);
        Console.WriteLine($"Passed {checks} shared controls checks. No Designer or visible window was started.");
    }
    static void DefaultConflictTests(Window window)
    {
        var keys = new KeyGesture(Key.F8, ModifierKeys.Control | ModifierKeys.Alt);
        var first = Commands.Register(new("collision/first", "First default", "Collision fixture", () => { }, () => true, keys));
        var second = Commands.Register(new("collision/second", "Second default", "Collision fixture", () => { }, () => true, keys));
        var hint = new MenuItem { Command = first.Command };
        ((MenuItem)window.FindName("mainMenu_Tools")).Items.Add(hint);
        var engine = ModControls.Bindings;
        var empty = new BindingSettings(new());
        engine.Apply(empty);
        Check(engine.Display(first.Definition.Id, empty.Overrides) == "" && engine.Display(second.Definition.Id, empty.Overrides) == "", "Competing mod defaults both stay inactive");
        Check(engine.DefaultConflicts(empty.Overrides).Count(c => c.CommandId.StartsWith("collision/")) == 2, "Default conflicts exposed through Core for any editor");
        Check(!window.InputBindings.OfType<KeyBinding>().Any(b => b.Command == first.Command || b.Command == second.Command), "Colliding defaults never create load-order winners");
        var explicitChoice = new BindingSettings(new() { [first.Definition.Id] = "Ctrl+Alt+F8" }); engine.Apply(explicitChoice);
        Check(engine.Display(first.Definition.Id, explicitChoice.Overrides) == "Ctrl+Alt+F8" && engine.Display(second.Definition.Id, explicitChoice.Overrides) == "", "Saved user assignment wins over another mod's default");
        var invalid = new BindingSettings(new() { [first.Definition.Id] = "Ctrl+Alt+F8", [second.Definition.Id] = "Ctrl+Alt+F8" });
        Reject(() => engine.Apply(invalid), "Two explicit assignments still require resolution");
        Check(hint.InputGestureText == "Ctrl+Alt+F8", "Invalid explicit bindings leave the prior live menu assignment intact");
        Check(!window.InputBindings.OfType<KeyBinding>().Any(b => b.Command == first.Command), "Single mod keys cannot bypass text guards through WPF command bindings");
        var plan = engine.Prepare(first.Definition.Id, null, new());
        Check(plan.Conflicts.Any(n => n.Contains("Second default")) && plan.Overrides[second.Definition.Id] == "", "Reset can review and claim an inactive default explicitly");
        engine.Apply(new(plan.Overrides, plan.EditorOverrides));
        Check(engine.Display(first.Definition.Id, plan.Overrides) == "Ctrl+Alt+F8", "Approved reset activates one winner");
        var native = new RoutedUICommand("Native collision", "NativeCollision", typeof(Program), new InputGestureCollection { keys });
        window.CommandBindings.Add(new(native, (_, _) => { }, (_, e) => e.CanExecute = true)); Commands.DiscoverNative(window);
        engine.Apply(empty);
        Check(native.InputGestures.Count == 1 && engine.Display(first.Definition.Id, empty.Overrides) == "", "Native keys are preserved when a mod requests them by default");
        var menu = (Menu)window.FindName("mainMenu"); menu.Items.Add(new MenuItem { Header = "_File" });
        var access = Commands.Register(new("collision/menu", "Menu collision", "Fixture", () => { }, () => true, new(Key.F, ModifierKeys.Alt)));
        Check(engine.DefaultConflicts(new()).Any(c => c.CommandId == access.Definition.Id && c.Owners.Any(n => n.Contains("menu access"))), "Native menu access keys participate in default conflict detection");
        Reject(() => engine.Validate(new(new() { [access.Definition.Id] = "Alt+F" })), "Direct assignments cannot bypass menu overwrite consent");
        Reject(() => engine.Validate(new(new() { [access.Definition.Id] = "Alt+F > P" })), "Chord prefixes also require menu overwrite consent");
        var menuReset = engine.Prepare(access.Definition.Id, null, new());
        Check(menuReset.Overrides.GetValueOrDefault(access.Definition.Id) == "Alt+F" && menuReset.EditorOverrides[access.Definition.Id] == "Alt+F", "Reviewed menu reset records an explicit assignment and consent");
        engine.Apply(new(menuReset.Overrides, menuReset.EditorOverrides));
        Check(engine.Display(access.Definition.Id, menuReset.Overrides) == "Alt+F", "Confirmed menu reset activates the requested default");
        engine.Apply(empty);
        var directInput = new KeyBinding(new RoutedCommand("Isolated editor", typeof(Program)), new KeyGesture(Key.F23, ModifierKeys.Control | ModifierKeys.Alt));
        window.InputBindings.Add(directInput);
        var direct = Commands.Register(new("collision/editor-default", "Editor default", "Fixture", () => { }, () => true, new(Key.F23, ModifierKeys.Control | ModifierKeys.Alt)));
        Reject(() => engine.Validate(new(new() { [direct.Definition.Id] = "Ctrl+Alt+F23" })), "Direct assignments cannot bypass editor overwrite consent");
        Reject(() => engine.Validate(new(new() { [direct.Definition.Id] = "Ctrl+Alt+F23 > P" })), "Chord prefixes also require editor overwrite consent");
        var editorReset = engine.Prepare(direct.Definition.Id, null, new());
        Check(editorReset.Overrides.GetValueOrDefault(direct.Definition.Id) == "Ctrl+Alt+F23" && editorReset.EditorOverrides[direct.Definition.Id] == "Ctrl+Alt+F23", "Reviewed editor reset records an explicit assignment and consent");
        engine.Apply(new(editorReset.Overrides, editorReset.EditorOverrides));
        Check(!window.InputBindings.Contains(directInput) && engine.Display(direct.Definition.Id, editorReset.Overrides) == "Ctrl+Alt+F23", "Confirmed editor reset activates its default and suppresses the prior binding");
        engine.Apply(empty);
        Check(window.InputBindings.Contains(directInput), "Removing an approved default takeover restores the editor binding");
        window.InputBindings.Remove(directInput);
        var directNative = new RoutedUICommand("Direct native", "DirectNative", typeof(Program));
        window.CommandBindings.Add(new(directNative, (_, _) => { }, (_, e) => e.CanExecute = true));
        var nativeInput = new KeyBinding(directNative, new KeyGesture(Key.F23, ModifierKeys.Control | ModifierKeys.Alt)); window.InputBindings.Add(nativeInput);
        Commands.DiscoverNative(window);
        Reject(() => engine.Validate(new(new() { [direct.Definition.Id] = "Ctrl+Alt+F23" })), "Native direct bindings absent from InputGestures still require consent");
        var nativeReset = engine.Prepare(direct.Definition.Id, null, new());
        engine.Apply(new(nativeReset.Overrides, nativeReset.EditorOverrides));
        Check(nativeReset.Conflicts.Any(n => n.Contains("Direct native")) && !window.InputBindings.Contains(nativeInput), "Reviewed transfer handles registered native direct bindings");
        engine.Apply(empty); Check(window.InputBindings.Contains(nativeInput), "Removing a native direct-binding takeover restores its original input");
        window.InputBindings.Remove(nativeInput);
        engine.Apply(empty);
    }
    static void MenuHintTests(Window window)
    {
        var menu = (Menu)window.FindName("mainMenu");
        var tools = (MenuItem)window.FindName("mainMenu_Tools");
        var programmer = new MenuItem { Header = "Programmer", InputGestureText = "Host hint" };
        int clicks = 0; programmer.Click += (_, _) => clicks++;
        var info = new RoutedUICommand("Info", "Info", typeof(Program));
        var infoMenu = new MenuItem { Header = "Info", Command = info };
        var preferences = new RoutedUICommand("Preferences", "Preferences", typeof(Program));
        var preferencesMenu = new MenuItem { Header = "Preferences", Command = preferences };
        tools.Items.Add(programmer); tools.Items.Add(infoMenu); tools.Items.Add(preferencesMenu);
        var prefsEntry = Commands.Register(new(Commands.NativeId(preferences), "Preferences", "Q-SYS", () => {}, () => true, Native: preferences));
        Commands.Register(new(Commands.NativeId(info), "Info", "Q-SYS", () => {}, () => true, Native: info));
        Mods.InitializingId = "menu-fixture"; Mods.States["menu-fixture"] = "Ready";
        var programAction = Commands.Register(new("menu-fixture/programmer", "Programmer", "Fixture", () => {}, () => true, new(Key.P, ModifierKeys.Control | ModifierKeys.Alt)));
        var infoAction = Commands.Register(new("menu-fixture/info", "Info", "Fixture", () => {}, () => true, new(Key.I, ModifierKeys.Control | ModifierKeys.Alt)));
        Commands.RegisterMenuHint(programmer, programAction.Definition.Id);
        Commands.RegisterMenuHint(infoMenu, infoAction.Definition.Id);
        Reject(() => Commands.RegisterMenuHint(infoMenu, prefsEntry.Definition.Id), "Hint cannot claim another provider's action");
        Mods.InitializingId = null;
        var view = new MenuItem { Header = "View" }; var submenu = new MenuItem { Header = "Sidebars" };
        var nestedAction = new MenuItem { Header = "Nested action", Command = programAction.Command };
        submenu.Items.Add(nestedAction); view.Items.Add(submenu); menu.Items.Add(view);
        var snapshot = ModControls.Current;
        var state = snapshot.Copy(); state.Bindings.Overrides[prefsEntry.Definition.Id] = "Ctrl+OemComma";
        ModControls.Bindings.Apply(state.Bindings);
        Check(preferencesMenu.InputGestureText == "Ctrl+,", "Menu punctuation must be readable instead of serialized OEM names");
        Check(programmer.InputGestureText == "Ctrl+Alt+P" && infoMenu.InputGestureText == "Ctrl+Alt+I", "Existing click and native menus show default mod shortcuts");
        Check(nestedAction.InputGestureText == "Ctrl+Alt+P", "Nested menus show current default bindings");
        Check(programmer.Command is null && ReferenceEquals(infoMenu.Command, info), "Hint registration preserves native commands and click handlers");
        programmer.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Check(clicks == 1, "Host click handler still executes once");
        state.Bindings.Overrides[programAction.Definition.Id] = "Ctrl+OemPeriod; Ctrl+NumPad0";
        state.Bindings.Overrides[infoAction.Definition.Id] = "";
        ModControls.Bindings.Apply(state.Bindings);
        Check(programmer.InputGestureText == "Ctrl+. or Ctrl+NumPad 0" && nestedAction.InputGestureText == programmer.InputGestureText, "Saved remaps and alternatives refresh every linked menu");
        Check(infoMenu.InputGestureText == "", "Clear removes the menu shortcut hint");
        state.Bindings.Overrides.Remove(infoAction.Definition.Id);
        ModControls.Bindings.Apply(state.Bindings);
        Check(infoMenu.InputGestureText == "Ctrl+Alt+I", "Reset restores the menu shortcut hint");
        Mods.States["menu-fixture"] = "Initialization failed"; ModControls.Bindings.Apply(state.Bindings);
        Check(programmer.InputGestureText == "Host hint", "A failed provider restores the original host hint");
        Mods.States["menu-fixture"] = "Ready"; ModControls.Bindings.Apply(snapshot.Bindings);
        Check(preferencesMenu.InputGestureText == "", "Removing a native remap restores the original hint");
        Check(ControlGestures.MenuText([new(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift), new(Key.Add, ModifierKeys.Control | ModifierKeys.Shift)]) == "Ctrl+Shift+= or Ctrl+Shift+NumPad +", "OEM plus and keypad plus remain distinct");
        Check(ControlGestures.MenuText([new(Key.Oem1, ModifierKeys.Control), new(Key.OemComma, ModifierKeys.Control), new(Key.OemComma, ModifierKeys.Control, "alias")]) == "Ctrl+; or Ctrl+,", "Literal semicolon and comma survive presentation and duplicate labels disappear");
        Check(ControlGestures.Serialize(new(Key.OemComma, ModifierKeys.Control)) == "Ctrl+OemComma", "Menu formatting never changes persisted key identity");
    }
    static void AliasTests(RoutedCommand native)
    {
        string id = Commands.NativeId(native), old = "alias-fixture/left";
        Mods.InitializingId = "alias-fixture"; Mods.States["alias-fixture"] = "Ready";
        Commands.RegisterNativeAlias(old, native);
        Commands.RegisterNativeAlias("alias-fixture/alternate", native);
        Reject(() => Commands.RegisterNativeAlias(old, native), "Duplicate alias rejected");
        Reject(() => Commands.RegisterNativeAlias("another-mod/left", native), "Alias must belong to declaring mod");
        Mods.InitializingId = null;
        Check(!Commands.All.Any(c => c.Definition.Id == old), "Alias creates no duplicate shortcut row or action");
        BindingSettings Migrate(Dictionary<string,string> keys, Dictionary<string,string>? consent = null) => ModControls.Bindings.MigrateAliases(new(keys, consent));
        var defaults = Migrate(new());
        Check(!defaults.Overrides.ContainsKey(id) && ModControls.Bindings.Display(id, defaults.Overrides) == "Ctrl+Shift+L", "No old assignment leaves actual native defaults intact");
        var original = new BindingSettings(new() { [old] = "Ctrl+Shift+L", [id] = "", ["absent/action"] = "Ctrl+F21" }, new() { [old] = "Ctrl+Shift+L", ["absent/action"] = "Ctrl+F21" });
        var moved = ModControls.Bindings.MigrateAliases(original);
        Check(moved.Overrides[id] == "Ctrl+Shift+L" && !moved.Overrides.ContainsKey(old), "Former duplicate overwrite restores the cleared native action");
        Check(moved.EditorOverrides![id] == "Ctrl+Shift+L" && !moved.EditorOverrides.ContainsKey(old), "Approved editor consent follows the transferred keys");
        Check(moved.Overrides["absent/action"] == "Ctrl+F21" && moved.EditorOverrides["absent/action"] == "Ctrl+F21", "Absent settings retained");
        Check(original.Overrides[id] == "" && original.Overrides.ContainsKey(old), "Migration does not mutate source settings");
        var again = ModControls.Bindings.MigrateAliases(moved);
        Check(again.Overrides.OrderBy(p=>p.Key).SequenceEqual(moved.Overrides.OrderBy(p=>p.Key)), "Migration is idempotent");
        var alternatives = Migrate(new() { [old] = "Ctrl+Alt+L" });
        Check(alternatives.Overrides[id] == "Ctrl+Shift+L; Ctrl+Alt+L", "Keep native defaults alongside a different old mod assignment");
        var merge = Migrate(new() { [id] = "Ctrl+Alt+L", [old] = "Ctrl+Alt+L; Ctrl+F23" });
        Check(merge.Overrides[id] == "Ctrl+Alt+L; Ctrl+F23", "Keep custom native alternatives and deduplicate identical keys");
        var cleared = Migrate(new() { [old] = "" });
        Check(!cleared.Overrides.ContainsKey(id) && !cleared.Overrides.ContainsKey(old), "Cleared standalone action must not clear native defaults");
        Check(Migrate(new() { [old] = "", [id] = "" }).Overrides[id] == "", "An explicit native clear is preserved");
        var many = Migrate(new() { [old] = "Ctrl+F23", ["alias-fixture/alternate"] = "Ctrl+F24", [id] = "" });
        var reversed = Migrate(new() { [id] = "", ["alias-fixture/alternate"] = "Ctrl+F24", [old] = "Ctrl+F23" });
        Check(many.Overrides[id] == reversed.Overrides[id] && ControlGestures.ParseList(many.Overrides[id]).Length == 2, "Multiple aliases merge independent of saved order");
        Mods.States["alias-fixture"] = "Initialization failed";
        Check(Migrate(new() { [old] = "Ctrl+F23" }).Overrides.ContainsKey(old), "Failed provider cannot consume legacy assignments");
        Mods.States["alias-fixture"] = "Ready";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "alias-migration-" + Guid.NewGuid().ToString("N"), "preferences.json");
        new SettingsStore(path).Save(new Dictionary<string, object> { [ModControls.SettingsId] = ControlSettings.Empty() with { Bindings = original } });
        var before = System.IO.File.ReadAllText(path);
        var loaded = ModControls.Load(new SettingsStore(path));
        Check(loaded.Bindings.Overrides[id] == "Ctrl+Shift+L" && System.IO.File.ReadAllText(path) == before, "Load migrates without writing the original preferences");
        var reset = ModControls.Bindings.Prepare(id, null, loaded.Bindings.Overrides, loaded.Bindings.EditorOverrides);
        Check(!reset.Overrides.ContainsKey(id) && !reset.Overrides.ContainsKey(old) && ModControls.Bindings.Display(id, reset.Overrides) == "Ctrl+Shift+L", "Reset restores native default without resurrecting duplicate");
        var clear = ModControls.Bindings.Prepare(id, "", loaded.Bindings.Overrides, loaded.Bindings.EditorOverrides);
        Check(clear.Overrides[id] == "" && !clear.EditorOverrides.ContainsKey(id), "Clear removes canonical key and editor consent");
    }
    static void GroupTests(Window window, Canvas canvas)
    {
        const string group = "grouped/default-controls";
        Mods.InitializingId = "grouped"; Mods.States["grouped"] = "Ready";
        ModControls.RegisterReplacementGroup(group, store => store.Read("group-fixture", () => true));
        var commands = Enumerable.Range(0, 4).Select(i => new RoutedUICommand("Group " + i, "Group " + i, typeof(Program),
            new InputGestureCollection { new KeyGesture(Key.F17 + i, ModifierKeys.Control | ModifierKeys.Shift) })).ToArray();
        int nativeRuns = 0, replacementRuns = 0;
        foreach (var command in commands) window.CommandBindings.Add(new CommandBinding(command, (_, _) => nativeRuns++, (_, e) => e.CanExecute = true));
        Commands.DiscoverNative(window);
        var members = commands.Select((command, i) => new ControlProposal("grouped/" + i, "grouped", "Grouped Mod", "1",
            Commands.NativeId(command), command.Text, "Schematic", Scope: ControlScope.Schematic) { ReplacementGroupId = group }).ToArray();
        foreach (var (proposal, command) in members.Zip(commands))
            ModControls.RegisterReplacement(proposal, command, () => new(ReplacementAvailability.Ready, canvas), _ => replacementRuns++);
        Mods.InitializingId = null;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qsys-group-tests-" + Guid.NewGuid().ToString("N"), "preferences.json");
        var fresh = ModControls.Load(new SettingsStore(path));
        Check(members.All(p => ControlPlans.Accepted(fresh, p)), "Fresh install enables all four grouped actions by default");
        Check(!ModControls.ReviewProposals.Any(p => p.ProviderId == "grouped"), "Feature-owned group never appears in first-run/per-action review");
        Check(!System.IO.File.Exists(path), "Default-on migration does not write preferences on load");
        var legacyOff = new SettingsStore(path); legacyOff.Save(new Dictionary<string, object> { ["group-fixture"] = false });
        Check(members.All(p => !ControlPlans.Accepted(ModControls.Load(new SettingsStore(path)), p)), "Legacy explicit opt-out preserved");
        var allOff = ControlSettings.Empty(); foreach (var p in members) allOff.Decisions[p.Id] = new("1", false);
        new SettingsStore(path).Save(new Dictionary<string, object> { ["group-fixture"] = true, [ModControls.SettingsId] = allOff });
        Check(!ModControls.GroupEnabled(ModControls.Load(new SettingsStore(path)), group), "Previous all-off per-action choice migrates to one opt-out");
        allOff.Decisions[members[0].Id] = new("1", true);
        new SettingsStore(path).Save(new Dictionary<string, object> { [ModControls.SettingsId] = allOff });
        Check(ModControls.GroupEnabled(ModControls.Load(new SettingsStore(path)), group), "Partial old selection becomes enabled as a whole");
        var remapped = fresh.Copy(); remapped.Bindings.Overrides[members[0].CommandId] = "Ctrl+Alt+F17";
        remapped.ReplacementGroups["absent/group"] = false;
        ModControls.Publish(remapped);
        var draft = new ControlDraft(remapped);
        var view = ControlGroupPreference.Create(draft, group, "Disable default Q-SYS control overrides");
        IEnumerable<DependencyObject> Walk(DependencyObject node)
        {
            yield return node;
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                foreach (var descendant in Walk(child)) yield return descendant;
        }
        var check = Walk(view).OfType<CheckBox>().Single();
        Check(check.IsChecked == false, "Opt-out is a single unchecked checkbox");
        check.IsChecked = true;
        Check(members.All(p => !ControlPlans.Accepted(draft.Value, p)), "Checking opt-out stages all four off");
        foreach (var command in commands) command.Execute(null, canvas);
        Check(replacementRuns == 4 && nativeRuns == 0, "Staged toggle does not change live routes before OK");
        Check(ModControls.Describe(Commands.All.Single(c => c.Definition.Id == members[0].CommandId), draft.Value).Source == "Q-SYS", "Shared draft immediately reports native source");
        Check(draft.Value.Bindings.Overrides[members[0].CommandId] == "Ctrl+Alt+F17", "Toggle preserves remapped keys");
        ModControls.Publish(draft.Value); foreach (var command in commands) command.Execute(null, canvas);
        Check(nativeRuns == 4 && replacementRuns == 4, "Published opt-out restores all four native actions");
        new SettingsStore(path).Save(new Dictionary<string, object> { [ModControls.SettingsId] = draft.Value });
        var reopened = ModControls.Load(new SettingsStore(path));
        Check(!ModControls.GroupEnabled(reopened, group) && reopened.ReplacementGroups.ContainsKey("absent/group"), "Opt-out and absent group choices survive save/reopen");
        check.IsChecked = false;
        Check(members.All(p => ControlPlans.Accepted(draft.Value, p)), "Unchecking opt-out stages all four on");
        Check(ModControls.Describe(Commands.All.Single(c => c.Definition.Id == members[0].CommandId), draft.Value).Source.Contains("Grouped Mod"), "Shared draft restores mod source");
        Reject(() => ModControls.Prepare(draft.Value, [members[0].Id], [members[0].Id]), "Partial group enable must be rejected");
        Mods.InitializingId = "competitor"; Mods.States["competitor"] = "Ready";
        var rival = members[0] with { Id = "competitor/one", ProviderId = "competitor", ProviderName = "Competitor", ReplacementGroupId = null };
        ModControls.RegisterReplacement(rival, commands[0], () => new(ReplacementAvailability.Ready, canvas), _ => { }); Mods.InitializingId = null;
        var transfer = ModControls.Prepare(draft.Value, [rival.Id], [rival.Id]);
        Check(transfer.Conflicts.Length > 0 && members.All(p => !ControlPlans.Accepted(transfer.Settings, p)), "Competing provider disables the whole group after overwrite");
        draft.Stage(transfer.Settings); check.IsChecked = false;
        Check(!ModControls.GroupEnabled(draft.Value, group) && Walk(view).OfType<TextBlock>().Any(t => t.Text.Contains("Competitor")), "Enable warns before displacing a competing provider");
        Walk(view).OfType<Button>().Single(b => Equals(b.Content, "Keep current")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(check.IsChecked == true && ControlPlans.Accepted(draft.Value, rival), "Canceled overwrite keeps the whole original choice");
        check.IsChecked = false;
        Walk(view).OfType<Button>().Single(b => Equals(b.Content, "Overwrite controls")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(members.All(p => ControlPlans.Accepted(draft.Value, p)) && !ControlPlans.Accepted(draft.Value, rival), "Confirmed overwrite enables the whole group and disables the rival");
        Check(draft.Value.Bindings.Overrides[members[0].CommandId] == "Ctrl+Alt+F17", "Provider overwrite does not rewrite keys");
        Mods.InitializingId = "other-group"; Mods.States["other-group"] = "Ready";
        ModControls.RegisterReplacementGroup("other-group/default", _ => true);
        var other = rival with { Id = "other-group/one", ProviderId = "other-group", ReplacementGroupId = "other-group/default" };
        ModControls.RegisterReplacement(other, commands[0], () => new(ReplacementAvailability.Ready, canvas), _ => { }); Mods.InitializingId = null;
        var freshPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qsys-group-conflict-" + Guid.NewGuid().ToString("N"), "preferences.json");
        var defaults = ModControls.Load(new SettingsStore(freshPath));
        Check(!ModControls.GroupEnabled(defaults, group) && !ModControls.GroupEnabled(defaults, "other-group/default"), "Conflicting fresh groups both stay off independent of registration order");
        new SettingsStore(freshPath).Save(new Dictionary<string, object> { [ModControls.SettingsId] = draft.Value });
        var existing = ModControls.Load(new SettingsStore(freshPath));
        Check(ModControls.GroupEnabled(existing, group) && !ModControls.GroupEnabled(existing, "other-group/default"), "New default preserves existing explicit group choice");
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
        Check(ModControls.Bindings.Display(failedDefault.Definition.Id, ModControls.Current.Bindings.Overrides) == "Ctrl+F10", "Core resolves registered defaults without Better Shortcuts");
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
