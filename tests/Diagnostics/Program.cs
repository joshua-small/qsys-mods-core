using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QsysMods.Core;

internal static class Program
{
    [STAThread] static void Main()
    {
        int checks=0;
        void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; Console.WriteLine("PASS "+name); }
        _=new Application();
        typeof(Mods).GetField("ready",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,new Window());
        Mods.Register("healthy",_=>{});
        Mods.Register("failed",_=>throw new InvalidOperationException("Test failure"));
        var entries=Mods.DiagnosticSnapshot();
        Check(entries.Count==2 && entries[0].Id=="failed","snapshot includes registered providers in stable order");
        Check(entries.Single(e=>e.Id=="healthy").Status=="Ready","successful initialization status");
        Check(entries[0].Status=="Initialization failed" && entries[0].Error=="Test failure","failure reason preserved without blocking another provider");
        Check(entries.All(e=>e.Assembly.Contains("Diagnostics")),"provider assembly identity included");
        DiagnosticsPage.Register();
        Check(Preferences.Id=="mod-diagnostics","page registers with valid settings identifier");
        var view=(ScrollViewer)Preferences.Factory!(new object()); var panel=(StackPanel)view.Content;
        Check(panel.Children.OfType<TextBox>().Single().IsReadOnly,"diagnostic path is read only");
        var list=panel.Children.OfType<StackPanel>().Single();
        Check(list.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("Test failure")),"page shows initialization failure");
        Mods.Register("later",_=>{});
        panel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(list.Children.Count==3,"refresh includes later registrations");
        Check(entries.Count==2,"previous snapshots remain immutable");
        Console.WriteLine($"{checks} diagnostics harness checks passed; no Designer or preferences files opened.");
    }
}
namespace Designer.Modules.Common { public interface IDesignerModule { } }
namespace QsysMods.Core
{
    internal static class Host { public static bool Enabled=>true; public static string SettingsDirectory=>"unused"; public static void Error(string id,Exception ex,bool notify=false){} public static void Receipt(string id,string name,object detail){} }
    internal static class Preferences { public static string? Id; public static Func<object,FrameworkElement>? Factory; public static void RegisterControlsPage(string id,string title,Func<object,FrameworkElement> factory) { SettingsStore.ValidateId(id); Id=id; Factory=factory; } public static void Install(){} public static void Open(){} }
    internal record CommandDefinition(string Id,string Name,string Group,Action Execute,Func<bool> CanExecute,bool InMenu);
    internal record Registered(CommandDefinition Definition);
    internal static class Commands { public static IEnumerable<Registered> All=>Array.Empty<Registered>(); public static ICommand Native(string name)=>ApplicationCommands.Open; public static void Initialize(Window window){} public static void Register(CommandDefinition definition){} }
    internal static class ModControls { public static void Initialize(Window window){} public static void CompleteInitialization(){} }
    internal static class SharedSettings { public static void Initialize(Window window){} }
}
