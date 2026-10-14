using System.Windows.Input;

namespace QsysMods.Core;

/// <summary>A physical stroke. Unlike KeyGesture, a chord completion may be an ordinary letter.</summary>
public readonly record struct ControlStroke(Key Key, ModifierKeys Modifiers)
{
    public static ControlStroke From(KeyGesture value) => new(value.Key, value.Modifiers);
    public bool IsModifier => Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
    public bool CanStart => !IsModifier && Key is not (Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed)
        && ((Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 || Key is >= Key.F1 and <= Key.F24);
    public string Serialize()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Windows");
        parts.Add(new KeyConverter().ConvertToInvariantString(Key)!);
        return string.Join("+", parts);
    }
    public string Label => ControlGestures.StrokeText(Key, Modifiers);
}

/// <summary>One combination or two sequential strokes. Semicolons separate alternatives; ' > ' separates strokes.</summary>
public sealed record ControlSequence(ControlStroke First, ControlStroke? Second = null)
{
    public bool IsChord => Second is not null;
    public string Serialize() => First.Serialize() + (Second is { } second ? " > " + second.Serialize() : "");
    public string Label => First.Label + (Second is { } second ? " then " + second.Label : "");
    public bool Conflicts(ControlSequence other) => First == other.First && (Second is null || other.Second is null || Second == other.Second);
    public static ControlSequence From(KeyGesture gesture) => new(ControlStroke.From(gesture));
    public static ControlSequence[] ParseList(string text) => string.IsNullOrWhiteSpace(text) ? [] : text.Split(';', StringSplitOptions.TrimEntries)
        .Select(Parse).Distinct().ToArray();
    public static ControlSequence Parse(string text)
    {
        var strokes = text.Split(" > ", StringSplitOptions.TrimEntries);
        if (strokes.Length is < 1 or > 2) throw new ArgumentException("Use one combination or a two-stroke chord.");
        return new(ParseStroke(strokes[0]), strokes.Length == 2 ? ParseStroke(strokes[1]) : null);
    }
    public static ControlStroke ParseStroke(string text)
    {
        var parts = text.Trim().Split('+');
        var modifiers = ModifierKeys.None;
        foreach (var part in parts.SkipLast(1)) modifiers |= part.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control" => ModifierKeys.Control, "alt" => ModifierKeys.Alt,
            "shift" => ModifierKeys.Shift, "win" or "windows" => ModifierKeys.Windows,
            _ => throw new ArgumentException("Invalid shortcut modifier.")
        };
        Key key;
        try { key = (Key)new KeyConverter().ConvertFromInvariantString(parts[^1].Trim())!; }
        catch (Exception ex) { throw new ArgumentException("Invalid shortcut key.", ex); }
        var stroke = new ControlStroke(key, modifiers);
        if (stroke.IsModifier || key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed || !Enum.IsDefined(key))
            throw new ArgumentException("Choose a key with its modifiers.");
        return stroke;
    }
    public void Validate(bool allowOrdinary = false)
    {
        if (!First.CanStart && !allowOrdinary) throw new ArgumentException("Start with Ctrl, Alt, or a function key so ordinary typing remains available.");
        if (Second is { Key: Key.Escape, Modifiers: ModifierKeys.None }) throw new ArgumentException("Escape is reserved for canceling a chord.");
    }
    public static string Join(IEnumerable<ControlSequence> values) => string.Join("; ", values.Distinct().Select(v => v.Serialize()));
}

public sealed record SequenceAssignment(string Id, string Name, ControlSequence[] Sequences);

public static class SequenceChanges
{
    public static BindingChange Prepare(Dictionary<string, string> draft, IReadOnlyDictionary<string, string?> requests,
        IReadOnlyDictionary<string, ControlSequence[]> desired, IEnumerable<SequenceAssignment> assignments,
        Dictionary<string, string>? editorOverrides = null)
    {
        var next = new Dictionary<string, string>(draft);
        var editors = new Dictionary<string, string>(editorOverrides ?? new());
        var owners = desired.SelectMany(p => p.Value.Select(s => (Id: p.Key, Sequence: s))).ToArray();
        for (int i = 0; i < owners.Length; i++)
            if (owners.Skip(i + 1).Any(o => o.Id != owners[i].Id && o.Sequence.Conflicts(owners[i].Sequence)))
                throw new ArgumentException("Selected actions request the same shortcut or a conflicting chord prefix: " + owners[i].Sequence.Label);
        foreach (var pair in requests)
        { editors.Remove(pair.Key); if (pair.Value is null) next.Remove(pair.Key); else next[pair.Key] = pair.Value; }
        var conflicts = new List<string>();
        foreach (var other in assignments)
        {
            // A batch swap uses the requested final state, not its old assignment.
            if (requests.ContainsKey(other.Id)) continue;
            var matching = other.Sequences.Where(s => owners.Any(o => o.Id != other.Id && o.Sequence.Conflicts(s))).ToArray();
            if (matching.Length == 0) continue;
            conflicts.Add(other.Name + " (" + string.Join(" or ", matching.Select(s => s.Label)) + ")");
            next[other.Id] = ControlSequence.Join(other.Sequences.Except(matching));
            if (editors.TryGetValue(other.Id, out var claimed))
            {
                var retained = ControlSequence.Join(ControlSequence.ParseList(claimed).Except(matching));
                if (retained.Length == 0) editors.Remove(other.Id); else editors[other.Id] = retained;
            }
        }
        return new(next, conflicts.Distinct().ToArray(), editors);
    }
}
