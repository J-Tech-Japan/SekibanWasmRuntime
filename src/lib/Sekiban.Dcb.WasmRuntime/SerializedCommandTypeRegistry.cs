using System.Collections.Frozen;
using System.Reflection;

namespace Sekiban.Dcb.WasmRuntime;

/// <summary>
///     Registry that maps command type names to their CLR types for generic deserialization.
///     Built at startup by the host application scanning domain assemblies.
/// </summary>
public class SerializedCommandTypeRegistry
{
    private readonly FrozenDictionary<string, Type> _commandTypes;

    public SerializedCommandTypeRegistry(IEnumerable<Type> commandTypes)
    {
        var dict = new Dictionary<string, Type>();
        foreach (var type in commandTypes)
        {
            dict[type.Name] = type;
        }
        _commandTypes = dict.ToFrozenDictionary();
    }

    public Type GetCommandType(string commandName)
    {
        if (!_commandTypes.TryGetValue(commandName, out var type))
        {
            throw new ArgumentException($"Unknown command type: {commandName}");
        }
        return type;
    }

    /// <summary>
    ///     Scans the given assemblies for all concrete types implementing
    ///     ICommandWithHandler&lt;T&gt; and builds a registry mapping type names to types.
    /// </summary>
    public static SerializedCommandTypeRegistry FromAssemblies(
        params Assembly[] assemblies)
    {
        var commandTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(IsCommandWithHandler);

        return new SerializedCommandTypeRegistry(commandTypes);
    }

    private static bool IsCommandWithHandler(Type type)
    {
        if (type.IsAbstract || type.IsInterface)
        {
            return false;
        }
        // Check by interface name to avoid direct dependency on Sekiban.Dcb.WithoutResult
        return type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            i.GetGenericTypeDefinition().FullName == "Sekiban.Dcb.Commands.ICommandWithHandler`1");
    }
}
