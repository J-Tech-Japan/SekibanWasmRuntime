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
            if (dict.TryGetValue(type.Name, out var existing))
            {
                throw new ArgumentException(
                    $"Duplicate command type name detected: {type.Name} ({existing.FullName} / {type.FullName})");
            }
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
        var allTypes = assemblies
            .SelectMany(a => a.GetTypes())
            .Distinct()
            .ToArray();

        var commandTypes = allTypes
            .Where(IsCommandWithHandler)
            .SelectMany(type => ExpandCommandType(type, allTypes));

        return new SerializedCommandTypeRegistry(commandTypes);
    }

    private static IEnumerable<Type> ExpandCommandType(Type type, IReadOnlyCollection<Type> candidateTypes)
    {
        if (!type.ContainsGenericParameters)
        {
            yield return type;
            yield break;
        }

        if (!type.IsGenericTypeDefinition)
        {
            yield break;
        }

        var genericArguments = type.GetGenericArguments();
        var resolvedArguments = new Type[genericArguments.Length];
        for (int index = 0; index < genericArguments.Length; index++)
        {
            var matches = candidateTypes
                .Where(candidate => SatisfiesConstraints(genericArguments[index], candidate))
                .ToArray();

            if (matches.Length == 0)
            {
                yield break;
            }

            if (matches.Length > 1)
            {
                throw new ArgumentException(
                    $"Generic command type '{type.FullName}' is ambiguous for parameter '{genericArguments[index].Name}': "
                    + string.Join(", ", matches.Select(static match => match.FullName)));
            }

            resolvedArguments[index] = matches[0];
        }

        yield return type.MakeGenericType(resolvedArguments);
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

    private static bool SatisfiesConstraints(Type genericParameter, Type candidate)
    {
        if (candidate.IsAbstract || candidate.IsInterface || candidate.ContainsGenericParameters)
        {
            return false;
        }

        GenericParameterAttributes attributes = genericParameter.GenericParameterAttributes;

        if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint) && candidate.IsValueType)
        {
            return false;
        }

        if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint) &&
            (!candidate.IsValueType || Nullable.GetUnderlyingType(candidate) is not null))
        {
            return false;
        }

        if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) &&
            candidate.GetConstructor(Type.EmptyTypes) is null)
        {
            return false;
        }

        return genericParameter
            .GetGenericParameterConstraints()
            .All(constraint => constraint.IsAssignableFrom(candidate));
    }
}
