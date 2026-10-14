using System.Windows.Input;

namespace QsysMods.Core;

public static class ControlGestures
{
    // DisplayString is presentation metadata, not a second binding. Do not let
    // KeyGestureConverter append it to the serialized key and modifiers.
    public static string Serialize(KeyGesture? gesture) => gesture is null ? "" :
        new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(gesture.Key, gesture.Modifiers))!;
    public static string Join(IEnumerable<KeyGesture> gestures) => string.Join("; ", gestures
        .DistinctBy(g => (g.Key, g.Modifiers)).Select(Serialize));
    public static KeyGesture[] ParseList(string text) => string.IsNullOrWhiteSpace(text) ? [] : text.Split(';')
        .Select(part => new KeyGestureConverter().ConvertFromInvariantString(part.Trim()) as KeyGesture
            ?? throw new ArgumentException("Invalid shortcut."))
        .DistinctBy(g => (g.Key, g.Modifiers)).ToArray();
    public static bool Same(KeyGesture left, KeyGesture right) => left.Key == right.Key && left.Modifiers == right.Modifiers;
}
