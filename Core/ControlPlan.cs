using System.Windows.Input;

namespace QsysMods.Core;

public sealed record BindingSettings(Dictionary<string, string> Overrides, Dictionary<string, string>? EditorOverrides = null);
public sealed record ControlDecision(string Revision, bool Accepted);
public sealed record ControlSettings(BindingSettings Bindings, Dictionary<string, ControlDecision> Decisions,
    Dictionary<string, string> Notified)
{
    public static ControlSettings Empty() => new(new(new()), new(), new());
    public ControlSettings Copy() => new(new(new(Bindings.Overrides), new(Bindings.EditorOverrides ?? new())), new(Decisions), new(Notified));
}

// These scopes are mutually exclusive host surfaces. Any overlaps every surface.
public enum ControlScope { Any, Schematic, Uci }
public sealed record ControlProposal(string Id, string ProviderId, string ProviderName, string Revision,
    string CommandId, string Name, string When, string? Binding = null, ControlScope Scope = ControlScope.Any)
{
    public bool IsReplacement => Binding is null;
}
public sealed record BindingAssignment(string Id, string Name, KeyGesture[] Gestures);
public sealed record BindingChange(Dictionary<string, string> Overrides, string[] Conflicts, Dictionary<string, string> EditorOverrides);
public sealed record ControlChange(ControlSettings Settings, string[] Conflicts);

public static class BindingChanges
{
    // Compute the entire desired set before transferring keys: A/B swaps must not
    // lose a binding just because one request happened to be enumerated first.
    public static BindingChange Prepare(Dictionary<string, string> draft, IReadOnlyDictionary<string, string?> requests,
        IReadOnlyDictionary<string, KeyGesture[]> desired, IEnumerable<BindingAssignment> assignments,
        Dictionary<string, string>? editorOverrides = null)
    {
        var next = new Dictionary<string, string>(draft);
        var editorNext = new Dictionary<string, string>(editorOverrides ?? new());
        var owners = new Dictionary<(Key, ModifierKeys), string>();
        foreach (var pair in desired)
            foreach (var gesture in pair.Value)
            {
                var key = (gesture.Key, gesture.Modifiers);
                if (owners.TryGetValue(key, out var other) && other != pair.Key)
                    throw new ArgumentException("Two selected actions request " + ControlGestures.Serialize(gesture) + ". Select only one of them.");
                owners[key] = pair.Key;
            }
        foreach (var pair in requests)
        {
            editorNext.Remove(pair.Key);
            if (pair.Value is null) next.Remove(pair.Key); else next[pair.Key] = pair.Value;
        }
        var conflicts = new List<string>();
        foreach (var other in assignments)
        {
            var matching = other.Gestures.Where(g => owners.TryGetValue((g.Key, g.Modifiers), out var owner) && owner != other.Id).ToArray();
            if (matching.Length == 0) continue;
            conflicts.Add(other.Name + " (" + ControlGestures.Join(matching) + ")");
            if (requests.ContainsKey(other.Id)) continue; // Its final state is already planned.
            next[other.Id] = ControlGestures.Join(other.Gestures.Where(g => !matching.Any(m => ControlGestures.Same(g, m))));
            if (editorNext.TryGetValue(other.Id, out var claimed))
            {
                var retained = ControlGestures.Join(ControlGestures.ParseList(claimed).Where(g => !matching.Any(m => ControlGestures.Same(g, m))));
                if (retained.Length == 0) editorNext.Remove(other.Id); else editorNext[other.Id] = retained;
            }
        }
        return new(next, conflicts.Distinct().ToArray(), editorNext);
    }
}

public static class ControlPlans
{
    public static bool Accepted(ControlSettings settings, ControlProposal proposal) =>
        settings.Decisions.TryGetValue(proposal.Id, out var decision) && decision.Revision == proposal.Revision && decision.Accepted;
    public static bool Reviewed(ControlSettings settings, ControlProposal proposal) =>
        settings.Decisions.TryGetValue(proposal.Id, out var decision) && decision.Revision == proposal.Revision;
    public static bool Overlaps(ControlProposal a, ControlProposal b) => a.CommandId == b.CommandId &&
        (a.Scope == b.Scope || a.Scope == ControlScope.Any || b.Scope == ControlScope.Any);

    public static ControlChange Prepare(ControlSettings current, ControlProposal[] all, string[] selectedIds,
        string[] reviewedIds, Func<IReadOnlyDictionary<string, string?>, BindingChange> bindings)
    {
        var next = current.Copy();
        var selected = selectedIds.Select(id => all.Single(p => p.Id == id)).ToArray();
        var replacements = selected.Where(p => p.IsReplacement).ToArray();
        for (int i = 0; i < replacements.Length; i++)
            if (replacements.Skip(i + 1).Any(p => Overlaps(p, replacements[i])))
                throw new ArgumentException("Select one replacement provider for each action and overlapping context.");
        var conflicts = new List<string>();
        foreach (var chosen in replacements)
            foreach (var other in all.Where(p => p.IsReplacement && p.Id != chosen.Id && Accepted(current, p) && Overlaps(chosen, p)))
            {
                next.Decisions[other.Id] = new(other.Revision, false);
                conflicts.Add(other.ProviderName + " — " + other.Name + " (replacement provider)");
            }
        var requested = new Dictionary<string, string?>();
        foreach (var item in selected.Where(p => !p.IsReplacement))
            if (!requested.TryAdd(item.CommandId, item.Binding))
                throw new ArgumentException("Select only one shortcut proposal for each action.");
        var change = bindings(requested);
        next = next with { Bindings = new(change.Overrides, change.EditorOverrides) };
        foreach (var id in reviewedIds)
        {
            var proposal = all.Single(p => p.Id == id);
            next.Decisions[id] = new(proposal.Revision, selectedIds.Contains(id));
            next.Notified[id] = proposal.Revision;
        }
        return new(next, conflicts.Concat(change.Conflicts).Distinct().ToArray());
    }
}

/// <summary>One draft shared by all pages in a Preferences dialog.</summary>
public sealed class ControlDraft
{
    public ControlSettings Value { get; private set; }
    public event Action? Changed;
    public ControlDraft(ControlSettings value) => Value = value.Copy();
    public void Stage(ControlSettings value) { Value = value.Copy(); Changed?.Invoke(); }
    public void StageBindings(BindingSettings value) => Stage(Value with { Bindings = value });
}
