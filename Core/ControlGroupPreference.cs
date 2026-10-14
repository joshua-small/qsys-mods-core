using System.Windows;
using System.Windows.Controls;

namespace QsysMods.Core;

/// <summary>A feature-owned, all-or-none opt-out; changes use the shared Preferences draft.</summary>
public static class ControlGroupPreference
{
    public static FrameworkElement Create(ControlDraft draft, string id, string disableLabel)
    {
        var panel = new StackPanel();
        var disable = new CheckBox { Content = disableLabel, Margin = new Thickness(0, 0, 0, 8) };
        var warning = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var actions = new WrapPanel { Visibility = Visibility.Collapsed };
        var overwrite = new Button { Content = "Overwrite controls", Padding = new Thickness(8), Margin = new Thickness(0, 8, 8, 8) };
        var keep = new Button { Content = "Keep current", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 8) };
        actions.Children.Add(overwrite); actions.Children.Add(keep);
        panel.Children.Add(disable); panel.Children.Add(warning); panel.Children.Add(actions);
        ControlChange? pending = null;
        bool refreshing = false;
        void Refresh()
        {
            refreshing = true; disable.IsChecked = !ModControls.GroupEnabled(draft.Value, id); refreshing = false;
            pending = null; warning.Text = ""; actions.Visibility = Visibility.Collapsed;
        }
        void Changed(object sender, RoutedEventArgs args)
        {
            if (refreshing) return;
            try
            {
                pending = ModControls.PrepareGroup(draft.Value, id, disable.IsChecked != true);
                ModControls.Validate(pending.Settings);
                if (pending.Conflicts.Length > 0)
                {
                    refreshing = true; disable.IsChecked = !ModControls.GroupEnabled(draft.Value, id); refreshing = false;
                    warning.Text = "Enabling these controls will replace:\n" + string.Join("\n", pending.Conflicts)
                        + "\nA mod's controls are switched together. Your shortcut keys stay assigned.";
                    actions.Visibility = Visibility.Visible;
                }
                else draft.Stage(pending.Settings);
            }
            catch (Exception ex) { Refresh(); warning.Text = ex.Message; }
        }
        disable.Checked += Changed; disable.Unchecked += Changed;
        overwrite.Click += (_, _) => { if (pending is { } change) draft.Stage(change.Settings); };
        keep.Click += (_, _) => Refresh();
        draft.Changed += Refresh;
        panel.Unloaded += (_, _) => draft.Changed -= Refresh;
        panel.Loaded += (_, _) => { draft.Changed -= Refresh; draft.Changed += Refresh; Refresh(); };
        Refresh(); return panel;
    }
}
