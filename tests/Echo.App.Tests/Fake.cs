using System.Reflection;

namespace echo.App.Tests;

/// <summary>Interface stub: returns configured values by member name, otherwise empty/default.</summary>
public class Fake<T> : DispatchProxy where T : class
{
    private Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public static T Create(Dictionary<string, object?>? values = null)
    {
        var proxy = DispatchProxy.Create<T, Fake<T>>();
        ((Fake<T>)(object)proxy)._values = values ?? new(StringComparer.Ordinal);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
        {
            return null;
        }

        var name = targetMethod.Name.StartsWith("get_", StringComparison.Ordinal)
            ? targetMethod.Name[4..]
            : targetMethod.Name;
        if (_values.TryGetValue(name, out var value))
        {
            return value;
        }

        var type = targetMethod.ReturnType;
        if (type == typeof(void))
        {
            return null;
        }

        if (type == typeof(string))
        {
            return string.Empty;
        }

        if (type.IsGenericType && type.GetGenericArguments().Length == 1
            && type.IsAssignableFrom(type.GetGenericArguments()[0].MakeArrayType()))
        {
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
