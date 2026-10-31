using System.Linq.Expressions;
using System.Reflection;

namespace ProtonNL.Hook;

internal static class Xaml
{
    public static Type Type(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(name);
            if (type != null)
                return type;
        }

        throw new InvalidOperationException("type not found: " + name);
    }

    public static Type? TryType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(name);
            if (type != null)
                return type;
        }

        return null;
    }

    public static object New(string typeName) => Activator.CreateInstance(Type(typeName))!;

    public static object Enum(string typeName, string value) => System.Enum.Parse(Type(typeName), value);

    public static void Set(object instance, string property, object? value)
    {
        PropertyInfo prop = instance.GetType().GetProperty(property)
            ?? throw new InvalidOperationException(instance.GetType().Name + "." + property);
        if (value != null && !prop.PropertyType.IsInstanceOfType(value) && prop.PropertyType.IsEnum)
            value = System.Enum.ToObject(prop.PropertyType, value);
        prop.SetValue(instance, value);
    }

    public static object? Get(object instance, string property)
    {
        return instance.GetType().GetProperty(property)?.GetValue(instance);
    }

    public static void Add(object parent, object child)
    {
        object children = Get(parent, "Children")
            ?? throw new InvalidOperationException(parent.GetType().Name + ".Children");
        MethodInfo add = children.GetType().GetMethod("Add", [child.GetType()])
            ?? children.GetType().GetMethods().First(m => m.Name == "Add" && m.GetParameters().Length == 1);
        add.Invoke(children, [child]);
    }

    public static void Clear(object parent)
    {
        object children = Get(parent, "Children")
            ?? throw new InvalidOperationException(parent.GetType().Name + ".Children");
        children.GetType().GetMethod("Clear")!.Invoke(children, null);
    }

    public static object Color(byte a, byte r, byte g, byte b)
    {
        MethodInfo fromArgb = Type("Windows.UI.Color").GetMethod("FromArgb", [typeof(byte), typeof(byte), typeof(byte), typeof(byte)])
            ?? throw new InvalidOperationException("Windows.UI.Color.FromArgb");
        return fromArgb.Invoke(null, [a, r, g, b])!;
    }

    public static object Brush(byte a, byte r, byte g, byte b)
    {
        Type brush = Type("Microsoft.UI.Xaml.Media.SolidColorBrush");
        return Activator.CreateInstance(brush, Color(a, r, g, b))!;
    }

    public static object Thickness(double all) => Activator.CreateInstance(Type("Microsoft.UI.Xaml.Thickness"), all)!;

    public static object Thickness(double l, double t, double r, double b)
        => Activator.CreateInstance(Type("Microsoft.UI.Xaml.Thickness"), l, t, r, b)!;

    public static object Corner(double all) => Activator.CreateInstance(Type("Microsoft.UI.Xaml.CornerRadius"), all)!;

    public static object GridLengthAuto()
    {
        return Type("Microsoft.UI.Xaml.GridLength").GetProperty("Auto")!.GetValue(null)!;
    }

    public static object GridLengthStar()
    {
        Type unit = Type("Microsoft.UI.Xaml.GridUnitType");
        return Activator.CreateInstance(Type("Microsoft.UI.Xaml.GridLength"), 1.0, System.Enum.Parse(unit, "Star"))!;
    }

    public static void Row(object grid, object length)
    {
        object defs = Get(grid, "RowDefinitions")!;
        object def = New("Microsoft.UI.Xaml.Controls.RowDefinition");
        Set(def, "Height", length);
        defs.GetType().GetMethod("Add")!.Invoke(defs, [def]);
    }

    public static void SetRow(object element, int row)
    {
        Type("Microsoft.UI.Xaml.Controls.Grid")
            .GetMethod("SetRow", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [element, row]);
    }

    public static void On(object source, string eventName, Action<object> handler)
    {
        EventInfo ev = source.GetType().GetEvent(eventName)
            ?? throw new InvalidOperationException(source.GetType().Name + "." + eventName);
        Type handlerType = ev.EventHandlerType
            ?? throw new InvalidOperationException(eventName + " has no handler type");
        MethodInfo invoke = handlerType.GetMethod("Invoke")
            ?? throw new InvalidOperationException(handlerType.FullName + ".Invoke");
        ParameterExpression[] pars = invoke.GetParameters()
            .Select(p => Expression.Parameter(p.ParameterType, p.Name))
            .ToArray();
        Expression sender = pars.Length == 0
            ? Expression.Constant(source, typeof(object))
            : Expression.Convert(pars[0], typeof(object));
        LambdaExpression lambda = Expression.Lambda(
            handlerType,
            Expression.Invoke(Expression.Constant(handler), sender),
            pars);
        ev.AddEventHandler(source, lambda.Compile());
    }

    public static bool Enqueue(object window, Action action)
    {
        object? queue = Get(window, "DispatcherQueue");
        if (queue == null)
            return false;

        Type handlerType = Type("Microsoft.UI.Dispatching.DispatcherQueueHandler");
        Delegate d = action.Target == null
            ? Delegate.CreateDelegate(handlerType, action.Method)
            : Delegate.CreateDelegate(handlerType, action.Target, action.Method);
        MethodInfo? tryEnqueue = queue.GetType().GetMethod("TryEnqueue", [handlerType]);
        return tryEnqueue?.Invoke(queue, [d]) is true;
    }
}
