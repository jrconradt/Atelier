using System.Diagnostics.CodeAnalysis;

namespace Atelier.Framework.Properties;

public class TypedPropertyBag
{
    private readonly Dictionary<string, object> _storage = new();
    private readonly Dictionary<string, Type> _schema = new();

    public void Set<T>(string key, T value) where T : notnull
    {
        if (_schema.ContainsKey(key) && _schema[key] != typeof(T))
        {
            throw new InvalidOperationException(
                $"Property '{key}' was registered as {_schema[key].Name} but attempted to set as {typeof(T).Name}");
        }

        _storage[key] = value;
        _schema[key] = typeof(T);
    }

    public T? Get<T>(string key)
    {
        if (!_storage.TryGetValue(key, out var value))
        {
            return default;
        }

        if (value is not T typedValue)
        {
            throw new InvalidCastException(
                $"Property '{key}' is of type {value.GetType().Name} but was requested as {typeof(T).Name}");
        }

        return typedValue;
    }

    public bool TryGet<T>(string key, [NotNullWhen(true)] out T? value)
    {
        if (_storage.TryGetValue(key, out var obj) && obj is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    public T? GetNullable<T>(string key) where T : struct
    {
        if (!_storage.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is not T typedValue)
        {
            throw new InvalidCastException(
                $"Property '{key}' is of type {value.GetType().Name} but was requested as {typeof(T).Name}");
        }

        return typedValue;
    }

    public bool Contains(string key) => _storage.ContainsKey(key);

    public bool ContainsKey(string key) => _storage.ContainsKey(key);

    public bool Remove(string key)
    {
        _schema.Remove(key);
        return _storage.Remove(key);
    }

    public const string RedactedPlaceholder = "***REDACTED***";

    public IReadOnlyDictionary<string, object> GetAll() => _storage;

    protected virtual bool IsSensitiveKey(string key) => false;

    public IReadOnlyDictionary<string, object> GetRedacted()
    {
        var redacted = new Dictionary<string, object>(_storage.Count);
        foreach (var kvp in _storage)
        {
            if (IsSensitiveKey(kvp.Key))
            {
                redacted[kvp.Key] = RedactedPlaceholder;
            }
            else
            {
                redacted[kvp.Key] = kvp.Value;
            }
        }

        return redacted;
    }

    public IReadOnlyDictionary<string, Type> GetSchema() => _schema;

    public void Clear()
    {
        _storage.Clear();
        _schema.Clear();
    }

    public int Count => _storage.Count;

    public IEnumerable<string> Keys => _storage.Keys;

    public T GetOrDefault<T>(string key, T defaultValue)
    {
        if (TryGet<T>(key, out var value))
        {
            return value;
        }

        return defaultValue;
    }

    public object? GetValueOrDefault(string key, object? defaultValue = null)
    {
        return _storage.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public T GetOrAdd<T>(string key, Func<T> factory) where T : notnull
    {
        if (TryGet<T>(key, out var existing))
        {
            return existing;
        }

        var value = factory();
        Set(key, value);
        return value;
    }

    public object? this[string key]
    {
        get => _storage.TryGetValue(key, out var value) ? value : null;
        set
        {
            if (value == null)
            {
                Remove(key);
                return;
            }

            _storage[key] = value;
            _schema[key] = value.GetType();
        }
    }

    public bool TryGetValue(string key, [NotNullWhen(true)] out object? value)
    {
        return _storage.TryGetValue(key, out value);
    }

    public static implicit operator TypedPropertyBag(Dictionary<string, object> dictionary)
    {
        var bag = new TypedPropertyBag();
        foreach (var kvp in dictionary)
        {
            bag[kvp.Key] = kvp.Value;
        }
        return bag;
    }

    public static implicit operator Dictionary<string, object>(TypedPropertyBag bag)
    {
        return new Dictionary<string, object>(bag._storage);
    }

    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>(_storage);
    }
}
