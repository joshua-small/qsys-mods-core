using System.Windows;
using System.Windows.Controls;

namespace QsysMods.Core;

internal static class DiagnosticsPage
{
    internal static void Register() => Preferences.RegisterControlsPage("mod-diagnostics", "Mod Diagnostics", _ => Create());
    private static FrameworkElement Create()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Initialization status for this Designer process. Ready confirms initialization, not every feature or connected device.", TextWrapping = TextWrapping.Wrap });
        var list = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
        void Refresh()
        {
            list.Children.Clear();
            foreach (var entry in Mods.DiagnosticSnapshot())
                list.Children.Add(new TextBlock { Text = $"{entry.Id}: {entry.Status}\n{entry.Assembly}{(entry.Error is null ? "" : "\n" + entry.Error)}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        }
        Refresh(); panel.Children.Add(list);
        var refresh = new Button { Content = "Refresh", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4) };
        refresh.Click += (_, _) => Refresh(); panel.Children.Add(refresh);
        panel.Children.Add(new TextBox { Text = System.IO.Path.Combine(Host.SettingsDirectory, "diagnostics"), IsReadOnly = true, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
