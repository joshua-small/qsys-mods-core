using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Threading;

namespace QsysMods.Core;

public sealed record ControlDescription(string Source, string When, string Details);

public static class ModControls
{
    public const string SettingsId = "mods-core-controls";
    private static readonly Dictionary<string, ControlProposal> proposals = new();
    private static readonly Dictionary<string, CommandReplacement> replacements = new();
    private static readonly Dictionary<string, bool> legacy = new();
    private static Window? main;
    private static bool completed, reviewing;
    private static ControlSettings current = ControlSettings.Empty();
    public static ControlBindings Bindings { get; private set; } = null!;
    public static ControlSettings Current => current.Copy();
    public static ControlProposal[] Proposals => proposals.Values.ToArray();
    public static event Action? Changed;

    internal static void Initialize(Window window)
    {
        main = window;
        Bindings = new(window);
        var router = new ReplacementRouter(window, () => replacements.Values.Where(r =>
            Mods.IsReady(r.Proposal.ProviderId) && ControlPlans.Accepted(current, r.Proposal)), ex => Host.Error("mods-core", ex, true));
        Preferences.RegisterControlsPage("mod-controls", "Mod Controls", draft => ControlReview.Create(draft));
        Commands.Register(new("mods-core/review-controls", "Review Mod Controls…", "Q-SYS Mods Core",
            () => Preferences.Open("mod-controls"), () => true, InMenu: true));
        Commands.Changed += RegistryChanged;
        window.Closed += (_, _) => { router.Dispose(); Bindings.Dispose(); Commands.Changed -= RegistryChanged; };
    }
    public static void RegisterReplacement(ControlProposal proposal, RoutedCommand native,
        Func<ReplacementContext> context, Action<object?> execute, bool? legacyAccepted = null)
    {
        if (!proposal.IsReplacement || proposal.CommandId != Commands.NativeId(native))
            throw new ArgumentException("Replacement must identify its native action.");
        Register(proposal);
        replacements.Add(proposal.Id, new(proposal, native, context, execute));
        if (legacyAccepted is { } value) legacy.Add(proposal.Id, value);
    }
    public static void RegisterShortcut(ControlProposal proposal)
    {
        if (proposal.IsReplacement) throw new ArgumentException("A shortcut proposal requires a binding.");
        if (!Commands.All.Any(c => c.Definition.Id == proposal.CommandId)) throw new ArgumentException("Register the action before proposing its shortcut.");
        foreach (var gesture in ControlGestures.ParseList(proposal.Binding!)) ControlBindings.Parse(ControlGestures.Serialize(gesture));
        Register(proposal);
    }
    private static void Register(ControlProposal proposal)
    {
        SettingsStore.ValidateId(proposal.ProviderId);
        if (Mods.InitializingId != proposal.ProviderId || string.IsNullOrWhiteSpace(proposal.Id)
            || !proposal.Id.StartsWith(proposal.ProviderId + "/", StringComparison.Ordinal)
            || !Enum.IsDefined(proposal.Scope) || string.IsNullOrWhiteSpace(proposal.Revision) || !proposals.TryAdd(proposal.Id, proposal))
            throw new ArgumentException("Invalid or duplicate control proposal.");
    }
    internal static ControlSettings Load(SettingsStore store)
    {
        var value = store.Read(SettingsId, () => new ControlSettings(
            store.Read("better-shortcuts", () => new BindingSettings(new())), new(), new()));
        if (value.Bindings?.Overrides is null || value.Decisions is null || value.Notified is null)
            throw new ArgumentException("Invalid shared control preferences. The original file was preserved.");
        var next = value.Copy();
        foreach (var pair in legacy.Where(p => !next.Decisions.ContainsKey(p.Key)))
        {
            var proposal = proposals[pair.Key];
            next.Decisions[pair.Key] = new(proposal.Revision, pair.Value);
            next.Notified[pair.Key] = proposal.Revision;
        }
        return next;
    }
    internal static void CompleteInitialization()
    {
        completed = false;
        Commands.DiscoverNative(main!);
        var value = Load(new SettingsStore(Preferences.PathName));
        Publish(value); completed = true;
        main!.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(Prompt));
    }
    private static void RegistryChanged()
    {
        if (!completed || Mods.InitializingId is not null) return;
        Publish(current);
    }
    public static void Validate(ControlSettings value)
    {
        if (value.Decisions is null || value.Notified is null || value.Decisions.Count > 10000 || value.Notified.Count > 10000)
            throw new ArgumentException("Invalid control decisions.");
        Bindings.Validate(value.Bindings);
        var selected = Proposals.Where(p => p.IsReplacement && ControlPlans.Accepted(value, p)).ToArray();
        for (int i = 0; i < selected.Length; i++)
            if (selected.Skip(i + 1).Any(p => ControlPlans.Overlaps(p, selected[i])))
                throw new ArgumentException("Multiple replacement providers selected for the same action and context.");
    }
    internal static void Publish(ControlSettings value)
    {
        var snapshot = value.Copy();
        Validate(snapshot); Bindings.Apply(snapshot.Bindings); current = snapshot;
        CommandManager.InvalidateRequerySuggested();
        if (main?.FindName("mainMenu") is ItemsControl menu)
            foreach (var item in Menus(menu).Where(m => m.Command is RoutedCommand c && c.Name == "mods-core/review-controls"))
            {
                int pending = Proposals.Count(p => Mods.IsReady(p.ProviderId) && !ControlPlans.Reviewed(current, p));
                item.Header = pending == 0 ? "Review Mod Controls…" : $"Review Mod Controls… ({pending} pending)";
            }
        foreach (var callback in Changed?.GetInvocationList() ?? [])
            try { ((Action)callback)(); } catch (Exception ex) { Host.Error("mods-core", ex); }
    }
    public static ControlChange Prepare(ControlSettings value, string[] selected, string[] reviewed) =>
        ControlPlans.Prepare(value, Proposals, selected, reviewed, requests => Bindings.Prepare(requests,
            value.Bindings.Overrides, value.Bindings.EditorOverrides));

    public static ControlDescription Describe(RegisteredCommand command, ControlSettings value)
    {
        var def = command.Definition;
        var entries = Proposals.Where(p => p.IsReplacement && p.CommandId == def.Id).ToArray();
        string source = def.Native is null ? def.Group : "Q-SYS";
        string when = def.When ?? "Designer decides availability in the focused editor";
        var details = new List<string>();
        foreach (var item in entries)
        {
            string status = !Mods.IsReady(item.ProviderId) ? Mods.Status(item.ProviderId)
                : ControlPlans.Accepted(value, item) ? "Active" : ControlPlans.Reviewed(value, item) ? "Replacement off" : "Awaiting review";
            details.Add(item.ProviderName + " — " + item.Name + ": " + status + ". " + item.When);
        }
        var enabled = entries.Where(p => Mods.IsReady(p.ProviderId) && ControlPlans.Accepted(value, p)).ToArray();
        if (enabled.Length > 0)
        {
            source = string.Join(" / ", enabled.Select(p => p.ProviderName + " (" + p.Scope.ToString().ToLowerInvariant() + ")").Distinct());
            if (!enabled.Any(p => p.Scope == ControlScope.Any)) source += " / Q-SYS (other contexts)";
            when = string.Join("; ", enabled.Select(p => p.When)) + (enabled.Any(p => p.Scope == ControlScope.Any) ? "" : "; other contexts: native Q-SYS rules");
            details.Add("Replaces the native action in the listed context; uses that action's current shortcuts and menu entries.");
        }
        if (Commands.ProviderId(def.Id) is { } provider && !Mods.IsReady(provider)) details.Add("Mod: " + Mods.Status(provider) + ".");
        return new(source, when, string.Join(" ", details));
    }
    private static void Prompt()
    {
        if (reviewing || main is not { IsEnabled: true }) return;
        var pending = Proposals.Where(p => Mods.IsReady(p.ProviderId) && !ControlPlans.Reviewed(current, p)
            && current.Notified.GetValueOrDefault(p.Id) != p.Revision).ToArray();
        if (pending.Length == 0) return;
        reviewing = true;
        try { ControlReview.Show(main, pending.Select(p => p.Id).ToArray()); }
        finally { reviewing = false; }
    }
    private static IEnumerable<MenuItem> Menus(ItemsControl parent)
    { foreach (var item in parent.Items.OfType<MenuItem>()) { yield return item; foreach (var child in Menus(item)) yield return child; } }
}
