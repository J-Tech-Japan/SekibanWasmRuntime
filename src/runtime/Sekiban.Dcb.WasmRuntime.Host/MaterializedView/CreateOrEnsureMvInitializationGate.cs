using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Sekiban.Dcb.MaterializedView;

namespace Sekiban.Dcb.WasmRuntime.Host.MaterializedView;

/// <summary>
///     CreateOrEnsure-only in-process gate: serializes <see cref="IMvExecutor.InitializeAsync"/> while
///     forwarding every other executor member through a Reflection.Emit proxy that preserves all
///     <see cref="PostgresMvExecutor"/> interfaces (including internal Orleans catch-up).
///     Delete when https://github.com/J-Tech-Japan/Sekiban/issues/1362 ships.
/// </summary>
public static class CreateOrEnsureMvInitializationGate
{
    private const string MaterializedViewAssemblySimpleName = "Sekiban.Dcb.MaterializedView";

    private static readonly ConstructorInfo AttributeParameterlessConstructor =
        typeof(Attribute).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null)
        ?? throw new InvalidOperationException("Failed to resolve Attribute parameterless constructor.");

    private static readonly MethodInfo RunInitializeAsyncMethod =
        typeof(CreateOrEnsureMvInitializationGate).GetMethod(
            nameof(RunInitializeAsync),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            [typeof(GateContext), typeof(IMvApplyHost), typeof(string), typeof(CancellationToken)],
            modifiers: null)
        ?? throw new InvalidOperationException("Failed to resolve RunInitializeAsync for the gate proxy.");

    private static readonly ConcurrentDictionary<string, Func<GateContext, IMvExecutor>> ProxyFactories = new();

    public static IMvExecutor Wrap(IMvExecutor inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var interfaces = inner.GetType().GetInterfaces();
        ValidateInterfaces(interfaces);

        var cacheKey = string.Join(
            "\0",
            interfaces.Select(static iface => iface.FullName).OrderBy(static name => name, StringComparer.Ordinal));
        var factory = ProxyFactories.GetOrAdd(cacheKey, _ => CreateProxyFactory(interfaces));
        return factory(new GateContext(inner));
    }

    public sealed class GateContext
    {
        public GateContext(IMvExecutor inner)
        {
            Inner = inner;
        }

        public IMvExecutor Inner { get; }

        public SemaphoreSlim InitializeGate { get; } = new(1, 1);
    }

    public static async Task RunInitializeAsync(
        GateContext context,
        IMvApplyHost host,
        string? serviceId,
        CancellationToken cancellationToken)
    {
        await context.InitializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await context.Inner.InitializeAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.InitializeGate.Release();
        }
    }

    private static void ValidateInterfaces(Type[] interfaces)
    {
        foreach (var interfaceType in interfaces)
        {
            foreach (var property in interfaceType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                throw new InvalidOperationException(
                    $"Cannot wrap IMvExecutor: interface '{interfaceType.FullName}' exposes property '{property.Name}'.");
            }

            foreach (var eventInfo in interfaceType.GetEvents(BindingFlags.Public | BindingFlags.Instance))
            {
                throw new InvalidOperationException(
                    $"Cannot wrap IMvExecutor: interface '{interfaceType.FullName}' exposes event '{eventInfo.Name}'.");
            }

            foreach (var method in interfaceType.GetMethods())
            {
                ValidateMethod(method, interfaceType);
            }
        }
    }

    private static void ValidateMethod(MethodInfo method, Type interfaceType)
    {
        if (method.IsGenericMethod)
        {
            throw new InvalidOperationException(
                $"Cannot wrap IMvExecutor: interface '{interfaceType.FullName}' exposes generic method '{method.Name}'.");
        }

        if (method.IsStatic)
        {
            throw new InvalidOperationException(
                $"Cannot wrap IMvExecutor: interface '{interfaceType.FullName}' exposes static method '{method.Name}'.");
        }

        if (method.ReturnType.IsByRef)
        {
            throw new InvalidOperationException(
                $"Cannot wrap IMvExecutor: interface '{interfaceType.FullName}' exposes by-ref returning method '{method.Name}'.");
        }

        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType.IsByRef)
            {
                throw new InvalidOperationException(
                    $"Cannot wrap IMvExecutor: interface '{interfaceType.FullName}' exposes by-ref parameter on method '{method.Name}'.");
            }
        }
    }

    private static bool IsExecutorInitializeAsync(MethodInfo method) =>
        method.Name == nameof(IMvExecutor.InitializeAsync) &&
        method.DeclaringType == typeof(IMvExecutor);

    private static Func<GateContext, IMvExecutor> CreateProxyFactory(Type[] interfaces)
    {
        var assemblyName = new AssemblyName($"SekibanWasmRuntime.MvInitGate.{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
        ApplyIgnoresAccessChecksTo(assemblyBuilder, moduleBuilder);
        var typeBuilder = moduleBuilder.DefineType(
            "CreateOrEnsureMvExecutorInitializationGateProxy",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);

        var contextField = typeBuilder.DefineField(
            "_context",
            typeof(GateContext),
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var innerField = typeBuilder.DefineField(
            "_inner",
            typeof(IMvExecutor),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        var constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(GateContext)]);
        var ctorIl = constructor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, contextField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Callvirt, typeof(GateContext).GetProperty(nameof(GateContext.Inner))!.GetMethod!);
        ctorIl.Emit(OpCodes.Stfld, innerField);
        ctorIl.Emit(OpCodes.Ret);

        foreach (var interfaceType in interfaces)
        {
            typeBuilder.AddInterfaceImplementation(interfaceType);
        }

        var emittedMethods = new Dictionary<string, MethodBuilder>(StringComparer.Ordinal);
        foreach (var interfaceType in interfaces)
        {
            foreach (var interfaceMethod in interfaceType.GetMethods())
            {
                ValidateMethod(interfaceMethod, interfaceType);
                var methodKey = BuildMethodKey(interfaceMethod);
                if (!emittedMethods.TryGetValue(methodKey, out var methodBuilder))
                {
                    methodBuilder = DefineForwarderMethod(
                        typeBuilder,
                        contextField,
                        innerField,
                        interfaceMethod,
                        IsExecutorInitializeAsync(interfaceMethod));
                    emittedMethods[methodKey] = methodBuilder;
                }

                typeBuilder.DefineMethodOverride(methodBuilder, interfaceMethod);
            }
        }

        var proxyType = typeBuilder.CreateType()
            ?? throw new InvalidOperationException("Failed to create the CreateOrEnsure MV initialization gate proxy type.");
        var proxyConstructor = proxyType.GetConstructor([typeof(GateContext)])
            ?? throw new InvalidOperationException("Failed to resolve the gate proxy constructor.");

        return context =>
        {
            var proxy = proxyConstructor.Invoke([context]);
            return (IMvExecutor)proxy;
        };
    }

    private static MethodBuilder DefineForwarderMethod(
        TypeBuilder typeBuilder,
        FieldBuilder contextField,
        FieldBuilder innerField,
        MethodInfo interfaceMethod,
        bool serializeInitializeAsync)
    {
        var parameterTypes = interfaceMethod.GetParameters().Select(static parameter => parameter.ParameterType).ToArray();
        var methodBuilder = typeBuilder.DefineMethod(
            $"Forward_{interfaceMethod.DeclaringType!.Name}_{interfaceMethod.Name}",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            interfaceMethod.ReturnType,
            parameterTypes);

        var il = methodBuilder.GetILGenerator();
        if (serializeInitializeAsync)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, contextField);
            for (var index = 0; index < parameterTypes.Length; index++)
            {
                il.Emit(OpCodes.Ldarg, index + 1);
            }

            il.Emit(OpCodes.Call, RunInitializeAsyncMethod);
            il.Emit(OpCodes.Ret);
            return methodBuilder;
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, innerField);
        for (var index = 0; index < parameterTypes.Length; index++)
        {
            il.Emit(OpCodes.Ldarg, index + 1);
        }

        il.Emit(OpCodes.Callvirt, interfaceMethod);
        il.Emit(OpCodes.Ret);
        return methodBuilder;
    }

    private static string BuildMethodKey(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parameterTypes = string.Join(
            ",",
            parameters.Select(static parameter => parameter.ParameterType.AssemblyQualifiedName));
        return $"{method.Name}({parameterTypes})";
    }

    private static void ApplyIgnoresAccessChecksTo(AssemblyBuilder assemblyBuilder, ModuleBuilder moduleBuilder)
    {
        var attributeType = DefineIgnoresAccessChecksToAttribute(moduleBuilder);
        var constructor = attributeType.GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException("IgnoresAccessChecksToAttribute constructor was not emitted.");

        var attributeBuilder = new CustomAttributeBuilder(
            constructor,
            [MaterializedViewAssemblySimpleName]);
        assemblyBuilder.SetCustomAttribute(attributeBuilder);
    }

    private static Type DefineIgnoresAccessChecksToAttribute(ModuleBuilder moduleBuilder)
    {
        var attributeTypeBuilder = moduleBuilder.DefineType(
            "System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(Attribute));

        var assemblyTargetConstructor = typeof(AttributeUsageAttribute).GetConstructor([typeof(AttributeTargets)])!;
        var allowMultipleProperty = typeof(AttributeUsageAttribute).GetProperty(nameof(AttributeUsageAttribute.AllowMultiple))!;
        var attributeUsageBuilder = new CustomAttributeBuilder(
            assemblyTargetConstructor,
            [AttributeTargets.Assembly],
            [allowMultipleProperty],
            [true]);
        attributeTypeBuilder.SetCustomAttribute(attributeUsageBuilder);

        var assemblyNameField = attributeTypeBuilder.DefineField(
            "_assemblyName",
            typeof(string),
            FieldAttributes.Private | FieldAttributes.InitOnly);
        var constructorBuilder = attributeTypeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(string)]);
        var ctorIl = constructorBuilder.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, AttributeParameterlessConstructor);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, assemblyNameField);
        ctorIl.Emit(OpCodes.Ret);

        return attributeTypeBuilder.CreateType()
            ?? throw new InvalidOperationException("Failed to create IgnoresAccessChecksToAttribute.");
    }
}
