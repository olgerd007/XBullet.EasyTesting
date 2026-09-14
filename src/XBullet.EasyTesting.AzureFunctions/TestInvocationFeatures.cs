using System.Collections;
using Microsoft.Azure.Functions.Worker;

namespace XBullet.EasyTesting.AzureFunctions;

internal sealed class TestInvocationFeatures : IInvocationFeatures
{
    private readonly Dictionary<Type, object> _features = [];

    public void Set<T>(T instance)
    {
        if (instance is null)
        {
            _features.Remove(typeof(T));
            return;
        }

        _features[typeof(T)] = instance;
    }

    public T? Get<T>() =>
        _features.TryGetValue(typeof(T), out var value) ? (T)value : default;

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => _features.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
