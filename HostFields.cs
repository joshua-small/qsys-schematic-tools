using System.Reflection;

namespace Designer.Modules.SelectionProbe;

internal static class HostFields
{
    internal static string OptionalText(object instance, string name)
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
