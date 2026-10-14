using System.Reflection;

namespace QsysMods.Core;

public static class HostFields
{
    public static string OptionalText(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return field.GetValue(instance) switch
        {
            null => "",
            string value => value,
            _ => throw new InvalidOperationException($"Field {instance.GetType().FullName}.{name} is not text.")
        };
    }
}
