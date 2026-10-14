using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Designer.Modules.Common;
using QsysMods.Core;

namespace Designer.Modules.ModsLabProbe;

public sealed class Probe : IDesignerModule
{
    private sealed record Config(string ApplicationDirectory, string OutputDirectory, bool DeveloperOnly = false);
    private Config? config;
    private DispatcherTimer? timer;
    private int attempts;
    public string Description => "Isolated mod migration checks";
    public string MenuTitle => "Mods lab check";
    public string Parent => "mainMenu_Tools";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };
    public Probe()
    {
        string path = Path.Combine(AppContext.BaseDirectory, ".mods-lab-probe.json");
        if (!Host.IsLab || !Host.Enabled || !File.Exists(path)) return;
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path))!;
        if (!Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(config.OutputDirectory, "result.json"))) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += async (_, _) => await Run(); timer.Start();
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static Task Idle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private static TabControl Tabs(Window window) => Preferences.Logical(window).OfType<TabControl>().Single();
    private static TabItem Page(Window window, string id) => Tabs(window).Items.OfType<TabItem>().Single(t => Equals(t.Tag, "qsys-mod:" + id));
    private static Selector Shortcuts(DependencyObject page) => Preferences.Logical(page).OfType<Selector>()
        .Single(s => s is ListBox || s is DataGrid { Name: "ShortcutTable" });
    private Window OpenPreferences()
    {
        var window = (Window)Activator.CreateInstance(Host.Type("QSC.QSys.windows.Dialogs.PreferencesDialog"))!;
        window.Owner = Application.Current.MainWindow; window.Show(); return window;
    }
    private void Render(Window window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96,96,PixelFormats.Pbgra32);
        bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(config!.OutputDirectory, name + ".png")); png.Save(output);
    }
    private async Task Run()
    {
        if (++attempts < 15 || Application.Current.MainWindow is not { IsLoaded:true, IsEnabled:true } main) return;
        if (Commands.All.Count < 20 && attempts < 40) return;
        timer!.Stop(); Directory.CreateDirectory(config!.OutputDirectory);
        var checks = new List<object>(); Window? prefs = null;
        try
        {
            Require(main.Title.StartsWith("untitled", StringComparison.OrdinalIgnoreCase), "Requires a blank isolated session.");
            if(config.DeveloperOnly)
            {
                Require(Host.DeveloperMode && Commands.All.Any(c=>c.Definition.Id=="schematic-tools/selection"),"Developer command did not register.");
                var app=Host.Type("QSC.QSys.App").GetField("AppInfo",Host.Static)!.GetValue(null)!;
                Require(Host.Property(app,"IsDevelopmentMode") is false,"The mod flag enabled native development mode.");
                Commands.All.Single(c=>c.Definition.Id=="schematic-tools/selection").Definition.Execute(); await Idle();
                var diagnostics = Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="Schematic Tools — Selection Diagnostics");
                var move = Preferences.Logical(diagnostics).OfType<Button>().Single(b=>Equals(b.Content,"Debug: Move selection down 20"));
                move.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Require(Preferences.Logical(diagnostics).OfType<TextBox>().Single().Text.StartsWith("No unlocked objects selected."), "Blank selection did not safely skip the debug mutation.");
                var blockedDirectory = Path.Combine(Host.SettingsDirectory,"diagnostics","receipt-check");
                File.WriteAllText(blockedDirectory,"Deliberately blocks a diagnostic directory in the isolated lab.");
                bool requiredFailed = false;
                try { Host.RequiredReceipt("receipt-check","before-mutation",new { }); } catch(IOException) { requiredFailed=true; }
                Require(requiredFailed && Host.Receipt("receipt-check","best-effort",new { }) == "", "Required receipt failure was swallowed.");
                diagnostics.Close();
                File.WriteAllText(Path.Combine(config.OutputDirectory,"result.json"),JsonSerializer.Serialize(new { success=true, customDeveloperFlag=true, nativeDevelopmentMode=false, diagnosticsOpened=true, debugEmptySelection=true, requiredReceiptFailure=true })); return;
            }
            Require(Commands.All.Any(c => c.Definition.Id == "better-packing/window") && Commands.All.Any(c => c.Definition.Id == "named-signal-colors/window"), "Independent operation commands missing.");
            Require(Commands.All.Any(c => c.Definition.Id == "better-shortcuts/all-sidebars"), "Shortcut mod missing.");
            var originalFile = File.Exists(Preferences.PathName) ? File.ReadAllText(Preferences.PathName) : null;
            prefs = OpenPreferences(); await Idle();
            Require(Tabs(prefs).Items.OfType<TabItem>().Count(t => t.Tag is string s && s.StartsWith("qsys-mod:")) == 3, "Expected three feature pages; no empty Mod Controls page.");
            Tabs(prefs).SelectedItem = Page(prefs, "better-packing"); await Idle();
            var gap = Preferences.Logical(Page(prefs, "better-packing")).OfType<TextBox>().Single();
            Require(gap.Text == "20", "Packing default differs."); gap.Text = "37";
            var disable = Preferences.Logical(Page(prefs, "better-packing")).OfType<CheckBox>().Single();
            Require(disable.IsChecked == false, "Packing overrides must default on."); disable.IsChecked = true;
            Render(prefs, "preferences-packing"); prefs.Close(); prefs = null; await Idle();
            Require((File.Exists(Preferences.PathName) ? File.ReadAllText(Preferences.PathName) : null) == originalFile, "Cancel wrote preferences."); checks.Add(new { cancelPreservedFile = true });
            prefs = OpenPreferences(); await Idle();
            gap = Preferences.Logical(Page(prefs, "better-packing")).OfType<TextBox>().Single(); Require(gap.Text == "20", "Cancel retained a staged value."); gap.Text = "37";
            disable = Preferences.Logical(Page(prefs, "better-packing")).OfType<CheckBox>().Single();
            Require(disable.IsChecked == false, "Cancel retained a staged opt-out."); disable.IsChecked = true;
            Require(Preferences.Logical(Page(prefs, "named-signal-colors")).OfType<CheckBox>().Single().IsChecked == false, "Only default colors must default false.");
            var save = Preferences.Logical(prefs).OfType<Button>().Single(b => b.IsDefault); save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); prefs = null; await Idle();
            var saved = File.ReadAllText(Preferences.PathName); Require(saved.Contains("37") && saved.Contains("false"), "OK did not persist preferences.");
            prefs = OpenPreferences(); await Idle(); Require(Preferences.Logical(Page(prefs, "better-packing")).OfType<TextBox>().Single().Text == "37", "Saved value did not reopen.");
            Require(Preferences.Logical(Page(prefs, "better-packing")).OfType<CheckBox>().Single().IsChecked == true, "Saved opt-out did not reopen.");
            Tabs(prefs).SelectedItem = Page(prefs, "better-shortcuts"); await Idle(); Render(prefs, "preferences-shortcuts");
            var shortcutPage = Page(prefs,"better-shortcuts");
            var list = Shortcuts(shortcutPage);
            if (list is DataGrid table)
            {
                Require(table.Columns.Select(c => c.Header.ToString()).SequenceEqual(new[] { "Command", "Keybinding", "When", "Source" }), "Shortcut table columns differ.");
                Require(table.Items.Cast<object>().Any(c => Host.Property(c,"Id").ToString()=="help/control-position"), "Official gesture reference missing.");
                table.SelectedItem = table.Items.Cast<object>().Single(c => Host.Property(c,"Id").ToString()=="help/control-position");
                Require(Preferences.Logical(shortcutPage).OfType<Button>().Where(b => b.Content is "Assign" or "Clear" or "Reset" or "Change shortcut").All(b => !b.IsEnabled), "Reference row allows assignment.");
                var search = Preferences.Logical(shortcutPage).OfType<TextBox>().Single(t => t.Name == "ShortcutSearch");
                search.Text = "Ctrl+Shift+Backspace";
                Require(table.Items.Count == 1 && Host.Property(table.Items[0],"Id").ToString()=="help/app-zoom-reset", "Shortcut search did not match the keybinding.");
                search.Text = "xyz-no-such-shortcut";
                Require(table.Items.Count == 0, "No-match search is not empty.");
                search.Clear();
                checks.Add(new { shortcutTableColumns=true, referenceRowsReadOnly=true, keybindingSearch=true });
            }
            list.SelectedItem = list.Items.Cast<object>().Single(c => Host.Property(c,"Id").ToString()=="better-shortcuts/dark-mode");
            bool directEditing = Preferences.Logical(shortcutPage).OfType<Button>().Any(b => Equals(b.Content, "Change shortcut"));
            void RecordGesture(string text)
            {
                if (directEditing) Click(shortcutPage, "Change shortcut");
                var recorder = Preferences.Logical(shortcutPage).OfType<FrameworkElement>().SingleOrDefault(e => e.Name == "ShortcutRecorder");
                if (recorder is null) { Preferences.Logical(shortcutPage).OfType<TextBox>().Last().Text = text; return; }
                var gesture = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(text)!;
                recorder.GetType().GetMethod("Begin", Host.Instance)!.Invoke(recorder, null);
                recorder.GetType().GetMethod("Capture", Host.Instance)!.Invoke(recorder, new object[] { gesture.Key, gesture.Modifiers, false });
                Require(Host.Property(recorder, "Gesture").ToString() == text, "Recorder did not capture the expected candidate.");
            }
            void Click(DependencyObject owner, string label) => Preferences.Logical(owner).OfType<Button>().Single(b=>Equals(b.Content,label)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            void ConfirmGesture()
            {
                if (!directEditing) { Click(shortcutPage, "Assign"); return; }
                var recorder = Preferences.Logical(shortcutPage).OfType<FrameworkElement>().Single(e => e.Name == "ShortcutRecorder");
                recorder.GetType().GetMethod("Confirm", Host.Instance)!.Invoke(recorder, null);
            }
            RecordGesture("Ctrl+Alt+0");
            if (directEditing)
            {
                Require(Preferences.Logical(shortcutPage).OfType<Button>().Any(b => Equals(b.Content, "Overwrite") && b.IsEnabled), "Conflict does not offer overwrite.");
                Click(shortcutPage, "Cancel");
                Require(Host.Property(list.SelectedItem!, "Keybinding").ToString() == "Ctrl+Alt+D", "Canceled conflict changed the target.");
                RecordGesture("Ctrl+Alt+0"); ConfirmGesture();
                Require(Host.Property(list.Items.Cast<object>().Single(c => Host.Property(c,"Id").ToString()=="better-shortcuts/all-sidebars"), "Keybinding").ToString()=="Unassigned", "Overwrite did not transfer the conflicting gesture.");
            }
            else
            {
                ConfirmGesture();
                Require(Preferences.Logical(shortcutPage).OfType<TextBlock>().Any(t=>t.Text.Contains("conflicts")),"Duplicate shortcut was not rejected.");
            }
            RecordGesture("Ctrl+Alt+F11"); ConfirmGesture();
            if (directEditing)
            {
                list.SelectedItem = list.Items.Cast<object>().Single(c => Host.Property(c,"Id").ToString()=="better-shortcuts/all-sidebars");
                Click(shortcutPage, "Reset");
            }
            Preferences.Logical(prefs).OfType<Button>().Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); prefs=null; await Idle();
            Require(main.InputBindings.OfType<KeyBinding>().Any(b=>b.Command is RoutedCommand c && c.Name=="better-shortcuts/dark-mode" && b.Key==Key.F11),"Custom shortcut did not apply.");
            prefs=OpenPreferences(); await Idle(); shortcutPage=Page(prefs,"better-shortcuts");
            list=Shortcuts(shortcutPage); list.SelectedItem=list.Items.Cast<object>().Single(c=>Host.Property(c,"Id").ToString()=="better-shortcuts/dark-mode");
            Click(shortcutPage,"Reset"); Preferences.Logical(prefs).OfType<Button>().Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); prefs=null; await Idle();
            Require(main.InputBindings.OfType<KeyBinding>().Any(b=>b.Command is RoutedCommand c && c.Name=="better-shortcuts/dark-mode" && b.Key==Key.D),"Shortcut reset did not restore the default.");
            checks.Add(new { shortcutConflictOverwrite=directEditing, shortcutConflictRejected=!directEditing, customShortcutSaved=true, resetRestoredDefault=true });
            checks.Add(new { nativePreferencesSaveAndReopen = true, defaultGap = 20, savedGap = 37, replaceDefault = true, onlyDefault = false });
            var registered = Commands.All.ToArray();
            File.WriteAllText(Path.Combine(config.OutputDirectory, "commands.json"), JsonSerializer.Serialize(registered.Select(c => new { c.Definition.Id,c.Definition.Name,c.Definition.Group, gesture = c.Definition.DefaultGesture?.GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture) }), new JsonSerializerOptions { WriteIndented = true }));
            var theme = registered.Single(c => c.Definition.Id == "better-shortcuts/dark-mode").Command;
            Require(theme.CanExecute(null, main), "Theme command is disabled.");
            registered.Single(c => c.Definition.Id == "snapshot-inspector/window").Command.Execute(null,main); await Idle();
            var initialSnapshot=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="Snapshot Inspector"); Render(initialSnapshot,"snapshot-initial-theme");
            theme.Execute(null, main); await Idle();
            try
            {
                prefs = OpenPreferences(); await Idle(); Tabs(prefs).SelectedItem = Page(prefs,"named-signal-colors"); await Idle(); Render(prefs, "preferences-opposite-theme"); prefs.Close(); prefs = null;
                var inspector = registered.Single(c => c.Definition.Id == "snapshot-inspector/window").Command; inspector.Execute(null, main); await Idle();
                var snapshot = Application.Current.Windows.OfType<Window>().Single(w => w.Title == "Snapshot Inspector"); Render(snapshot, "snapshot-opposite-theme");
                Require(ReferenceEquals(snapshot,initialSnapshot),"Expected the same window across theme changes.");
                Require(snapshot.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue, "Snapshot has a fixed background."); snapshot.Close();
            }
            finally { theme.Execute(null, main); await Idle(); }
            checks.Add(new { registeredCommands = registered.Length, themeToggleRoundTrip = true, snapshotUsesHostTheme = true });
            var panels = new[] { "_toolWindow_Inventory", "_toolWindow_Properties", "_toolWindow_Library" }.Select(n => Host.Field(Host.Field(main,"_DocHost"),n)).ToArray();
            var before = panels.Select(p => Host.Property(p,"State").ToString()).ToArray();
            var all = registered.Single(c => c.Definition.Id == "better-shortcuts/all-sidebars").Command;
            Require(before.All(s => s == "Docked"), "Expected the lab's initial docked sidebars.");
            try { all.Execute(null,main); await Idle(); Require(panels.All(p => Host.Property(p,"State").ToString() == "AutoHide"), "All sidebars did not collapse."); }
            finally { all.Execute(null,main); await Idle(); }
            Require(panels.All(p => Host.Property(p,"State").ToString() == "Docked"), "All sidebars did not restore.");
            checks.Add(new { allSidebarsRoundTrip = true });
            var design=Host.Field(Host.Field(main,"_DocHost"),"_DesignElements");
            var expanded=(DependencyProperty)Host.Type("QSC.QSys.windows.AccordionPanel").GetField("IsExpandedProperty",Host.Static)!.GetValue(null)!;
            var focusCases=new[] { ("inventory","_InventoryViewer"), ("schematic-pages","_PageViewer"), ("uci","_UciViewer"), ("snapshots","_SnapshotViewer"), ("named-controls","_ExternalControlViewer"), ("inspector","_Inspector") };
            var expansion=focusCases.Select(c=>((DependencyObject)Host.Field(design,c.Item2)).GetValue(expanded)).ToArray();
            main.Activate(); await Idle();
            try
            {
                foreach(var (id,field) in focusCases)
                {
                    registered.Single(c=>c.Definition.Id=="better-shortcuts/focus-"+id).Definition.Execute(); await Idle();
                    Require(((UIElement)Host.Field(design,field)).IsKeyboardFocusWithin,"Focus did not enter "+id);
                }
                registered.Single(c=>c.Definition.Id=="better-shortcuts/focus-schematic-search").Definition.Execute(); await Idle();
                Require(Keyboard.FocusedElement is TextBox,"Schematic search did not receive text focus.");
                checks.Add(new { sidebarFocusCommands=6, schematicSearchFocus=true });
            }
            finally { for(int i=0;i<focusCases.Length;i++) ((DependencyObject)Host.Field(design,focusCases[i].Item2)).SetValue(expanded,expansion[i]); }
            File.WriteAllText(Path.Combine(config.OutputDirectory,"result.json"), JsonSerializer.Serialize(new { success = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(config.OutputDirectory,"result.json"), JsonSerializer.Serialize(new { success = false, error = ex.ToString(), checks }, new JsonSerializerOptions { WriteIndented = true })); }
        finally { prefs?.Close(); }
    }
}
