using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace QsysMods.Core;

internal static class ControlReview
{
    internal static FrameworkElement Create(ControlDraft draft, string[]? only = null, Action? applied = null)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Review mod controls", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap,
            Text = "Choose which actions mods replace and which shortcuts to change. Replacing an action keeps its current keys. Uncheck a replacement to turn it off; unchecked shortcut proposals leave current keys alone. Conflicts can be overwritten after review." });
        var rows = new StackPanel();
        panel.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var message = new TextBlock { Margin = new Thickness(0, 12, 0, 10), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        panel.Children.Add(new ScrollViewer { Content = message, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var actions = new WrapPanel(); panel.Children.Add(actions);
        var apply = new Button { Content = "Apply selected", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        var all = new Button { Content = "Apply all", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        var keep = new Button { Content = "Keep current", Padding = new Thickness(10, 5, 10, 5) };
        actions.Children.Add(apply); actions.Children.Add(all); actions.Children.Add(keep);
        var checks = new Dictionary<string, CheckBox>();
        var displayed = ModControls.ReviewProposals.Where(p => only is null || only.Contains(p.Id)).ToArray();
        ControlChange? pending = null;
        void ResetWarning() { pending = null; apply.Content = "Apply selected"; message.Text = applied is null ? "Changes are staged until Preferences OK. Cancel discards them." : "Review your choices, then apply them together."; }
        void Populate()
        {
            checks.Clear(); rows.Children.Clear(); ResetWarning();
            foreach (var group in displayed.GroupBy(p => p.ProviderName))
            {
                rows.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
                foreach (var item in group)
                {
                    var check = new CheckBox { Content = item.Name, IsEnabled = Mods.IsReady(item.ProviderId),
                        IsChecked = ControlPlans.Accepted(draft.Value, item) || !ControlPlans.Reviewed(draft.Value, item), Margin = new Thickness(0, 5, 0, 2) };
                    var current = ModControls.Bindings.Display(item.CommandId, draft.Value.Bindings.Overrides);
                    string detail = item.IsReplacement
                        ? "Use " + item.ProviderName + " instead of Q-SYS. Keys stay " + (current.Length == 0 ? "unassigned" : current) + ". Menus use the same action. " + item.When
                        : "Shortcut: " + (current.Length == 0 ? "Unassigned" : current) + " → " + (item.Binding!.Length == 0 ? "Unassigned" : item.Binding) + ". " + item.When;
                    if (!Mods.IsReady(item.ProviderId)) detail += " Mod: " + Mods.Status(item.ProviderId) + ".";
                    else if (item.IsReplacement) detail += ControlPlans.Accepted(draft.Value, item) ? " Replacement active." : " Replacement off.";
                    rows.Children.Add(check);
                    rows.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0, 0, 6) });
                    checks.Add(item.Id, check);
                    check.Checked += (_, _) => ResetWarning(); check.Unchecked += (_, _) => ResetWarning();
                }
            }
            if (displayed.Length == 0) message.Text = "No installed mods have proposed control changes.";
            apply.IsEnabled = all.IsEnabled = keep.IsEnabled = displayed.Any(p => Mods.IsReady(p.ProviderId));
        }
        void Apply()
        {
            try
            {
                if (pending is null)
                {
                    pending = ModControls.Prepare(draft.Value, checks.Where(p => p.Value.IsEnabled && p.Value.IsChecked == true).Select(p => p.Key).ToArray(),
                        checks.Where(p => p.Value.IsEnabled).Select(p => p.Key).ToArray());
                    ModControls.Validate(pending.Settings);
                    if (pending.Conflicts.Length > 0)
                    {
                        message.Text = "These assignments will be overwritten:\n" + string.Join("\n", pending.Conflicts.Select(c => "• " + c))
                            + "\n\nOther shortcuts stay assigned. Choose Overwrite and apply to confirm, or change your selection.";
                        apply.Content = "Overwrite and apply"; apply.Focus(); return;
                    }
                }
                draft.Stage(pending.Settings); pending = null; applied?.Invoke();
                message.Text = applied is null ? "Staged. Preferences OK saves; Cancel discards changes." : "Applied.";
            }
            catch (Exception ex) { pending = null; apply.Content = "Apply selected"; message.Text = ex.Message; }
        }
        apply.Click += (_, _) => Apply();
        all.Click += (_, _) => { foreach (var check in checks.Values.Where(c => c.IsEnabled)) check.IsChecked = true; pending = null; Apply(); };
        keep.Click += (_, _) =>
        {
            try
            {
                var next = draft.Value.Copy();
                foreach (var item in displayed.Where(p => Mods.IsReady(p.ProviderId) && !ControlPlans.Reviewed(next, p)))
                { next.Decisions[item.Id] = new(item.Revision, false); next.Notified[item.Id] = item.Revision; }
                draft.Stage(next); applied?.Invoke();
                message.Text = applied is null ? "Current controls kept in this draft. Preferences OK saves." : "Current controls kept.";
            }
            catch (Exception ex) { message.Text = ex.Message; }
        };
        // Shared draft events keep this page current after edits in Better Shortcuts.
        draft.Changed += Populate;
        panel.Unloaded += (_, _) => draft.Changed -= Populate;
        panel.Loaded += (_, _) => { draft.Changed -= Populate; draft.Changed += Populate; Populate(); };
        Populate(); return panel;
    }
    internal static void Show(Window owner, string[] ids)
    {
        var store = new SettingsStore(Preferences.PathName);
        var draft = new ControlDraft(ModControls.Load(store));
        var window = new Window { Title = "Q-SYS Mods — Review controls", Owner = owner, Width = 850, Height = 650,
            MinWidth = 520, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Host.Theme(window);
        bool saved = false;
        void Save()
        {
            var previous = ModControls.Current;
            ModControls.Validate(draft.Value); ModControls.Publish(draft.Value);
            try { store.Save(new Dictionary<string, object> { [ModControls.SettingsId] = draft.Value }); }
            catch { ModControls.Publish(previous); throw; }
            saved = true; window.Close();
        }
        var body = new DockPanel();
        var later = new Button { Content = "Review later", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(later, Dock.Bottom); body.Children.Add(later);
        body.Children.Add(new ScrollViewer { Content = Create(draft, ids, Save), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        later.Click += (_, _) => window.Close(); window.Content = body;
        window.Closed += (_, _) =>
        {
            if (saved) return;
            // Dismissal records only notification delivery, never approves a proposal.
            try
            {
                var value = ModControls.Current;
                foreach (var proposal in ModControls.Proposals.Where(p => ids.Contains(p.Id))) value.Notified[proposal.Id] = proposal.Revision;
                store.Save(new Dictionary<string, object> { [ModControls.SettingsId] = value });
                ModControls.Publish(value);
            }
            catch (Exception ex) { Host.Error("mods-core", ex); }
        };
        window.ShowDialog();
    }
}
