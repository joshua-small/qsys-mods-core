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

    // Presentation only: retain invariant WPF names in saved settings. Like the
    // shortcut table, punctuation uses US key labels and keeps keypad keys distinct.
    public static string MenuText(IEnumerable<KeyGesture> gestures) => string.Join(" or ", gestures
        .DistinctBy(g => (g.Key, g.Modifiers)).Select(g => StrokeText(g.Key, g.Modifiers)));
    public static string StrokeText(Key key, ModifierKeys modifiers)
    {
            var parts = new List<string>();
            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(key switch
            {
                Key.OemComma => ",", Key.OemPeriod => ".", Key.OemPlus => "=", Key.OemMinus => "-",
                Key.Oem1 => ";", Key.Oem2 => "/", Key.Oem3 => "`", Key.Oem4 => "[", Key.Oem5 => "\\",
                Key.Oem6 => "]", Key.Oem7 => "'", Key.Oem102 => "Intl \\",
                Key.Add => "NumPad +", Key.Subtract => "NumPad -", Key.Multiply => "NumPad *",
                Key.Divide => "NumPad /", Key.Decimal => "NumPad .", Key.Separator => "NumPad Separator",
                Key.Back => "Backspace", Key.Return => "Enter", Key.Prior => "Page Up", Key.Next => "Page Down",
                Key.Left => "Left Arrow", Key.Right => "Right Arrow", Key.Up => "Up Arrow", Key.Down => "Down Arrow",
                >= Key.NumPad0 and <= Key.NumPad9 => "NumPad " + ((int)key - (int)Key.NumPad0),
                >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
                _ => key.ToString()
            });
            return string.Join("+", parts);
    }
}
