using Sekiban.Dcb.Primitives;

namespace SekibanWasm.Tests;

public class FakePrimitiveProjectionHost : IPrimitiveProjectionHost
{
    private readonly Dictionary<string, Func<FakePrimitiveProjectionInstance>> _factories = new();

    public void RegisterProjector(string projectorName, Func<FakePrimitiveProjectionInstance>? factory = null)
    {
        _factories[projectorName] = factory ?? (() => new FakePrimitiveProjectionInstance());
    }

    public IPrimitiveProjectionInstance CreateInstance(string projectorName)
    {
        if (_factories.TryGetValue(projectorName, out var factory))
        {
            return factory();
        }
        throw new InvalidOperationException($"No factory registered for projector '{projectorName}'");
    }
}
