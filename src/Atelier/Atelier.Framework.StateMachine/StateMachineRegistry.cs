using Atelier.Framework.Primitives;
using System.Collections.Concurrent;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.StateMachine.Service;

[Infrastructure(InfrastructureLifetime.Singleton)]
public class StateMachineRegistry : IStateMachineRegistry
{
    private readonly ConcurrentDictionary<string, IStateMachineInstance> _instances = new();
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, byte>> _instancesByType = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _instancesByTag = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _tagKeysByInstance = new();

    public int Count => _instances.Count;

    public Task<Outcome> Register(string instanceId, IStateMachineInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(instance);

        if (_instances.TryGetValue(instanceId, out var existing))
        {
            RemoveFromTypeIndex(instanceId, existing.Type);
            RemoveFromTagIndex(instanceId);
        }

        _instances[instanceId] = instance;

        var typeSet = _instancesByType.GetOrAdd(
            instance.Type,
            _ => new ConcurrentDictionary<string, byte>());
        typeSet[instanceId] = 0;

        var tagKeys = new List<string>();
        foreach (var tag in instance.Tags)
        {
            var tagKey = $"{tag.Key}:{tag.Value}";
            var tagSet = _instancesByTag.GetOrAdd(
                tagKey,
                _ => new ConcurrentDictionary<string, byte>());
            tagSet[instanceId] = 0;
            tagKeys.Add(tagKey);
        }

        _tagKeysByInstance[instanceId] = tagKeys;

        return Task.FromResult(Outcome.Success());
    }

    private void RemoveFromTypeIndex(string instanceId, Type type)
    {
        ArgumentNullException.ThrowIfNull(instanceId);
        ArgumentNullException.ThrowIfNull(type);

        if (_instancesByType.TryGetValue(type, out var typeSet))
        {
            typeSet.TryRemove(instanceId, out _);
            if (typeSet.IsEmpty)
            {
                _instancesByType.TryRemove(type, out _);
            }
        }
    }

    private void RemoveFromTagIndex(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        if (!_tagKeysByInstance.TryRemove(instanceId, out var tagKeys))
        {
            return;
        }

        foreach (var tagKey in tagKeys)
        {
            if (_instancesByTag.TryGetValue(tagKey, out var tagSet))
            {
                tagSet.TryRemove(instanceId, out _);
                if (tagSet.IsEmpty)
                {
                    _instancesByTag.TryRemove(tagKey, out _);
                }
            }
        }
    }

    public Task<Outcome> Unregister(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return Task.FromResult(Outcome.Success());
        }

        if (_instances.TryRemove(instanceId, out var instance))
        {
            RemoveFromTypeIndex(instanceId, instance.Type);
            RemoveFromTagIndex(instanceId);
        }

        return Task.FromResult(Outcome.Success());
    }

    public Task<Outcome<IStateMachineInstance>> GetInstance(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        if (_instances.TryGetValue(instanceId, out var instance))
        {
            return Task.FromResult(Outcome<IStateMachineInstance>.Success(instance));
        }

        return Task.FromResult(Outcome<IStateMachineInstance>.Failure());
    }

    public Task<Outcome<IEnumerable<IStateMachineInstance>>> GetAllInstances()
    {
        return Task.FromResult(Outcome<IEnumerable<IStateMachineInstance>>.Success(_instances.Values.ToArray()));
    }

    public Task<Outcome<IEnumerable<IStateMachineInstance>>> GetInstancesByType<T>() where T : class
    {
        if (_instancesByType.TryGetValue(typeof(T), out var instanceIds))
        {
            return Task.FromResult(Outcome<IEnumerable<IStateMachineInstance>>.Success(
                instanceIds.Keys
                    .Select(id => _instances.TryGetValue(id, out var instance) ? instance : null)
                    .Where(instance => instance != null)!));
        }

        return Task.FromResult(Outcome<IEnumerable<IStateMachineInstance>>.Success(
            Enumerable.Empty<IStateMachineInstance>()));
    }

    public Task<Outcome<IEnumerable<IStateMachineInstance>>> GetInstancesByTag(string tag, string value)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Task.FromResult(Outcome<IEnumerable<IStateMachineInstance>>.Failure());
        }

        if (value is null)
        {
            return Task.FromResult(Outcome<IEnumerable<IStateMachineInstance>>.Failure());
        }

        var tagKey = $"{tag}:{value}";

        if (_instancesByTag.TryGetValue(tagKey, out var instanceIds))
        {
            return Task.FromResult(Outcome<IEnumerable<IStateMachineInstance>>.Success(
                instanceIds.Keys
                    .Select(id => _instances.TryGetValue(id, out var instance) ? instance : null)
                    .Where(instance => instance != null)!));
        }

        return Task.FromResult(Outcome<IEnumerable<IStateMachineInstance>>.Success(
            Enumerable.Empty<IStateMachineInstance>()));
    }

    public Task<Outcome> IsRegistered(string instanceId)
    {
        ArgumentNullException.ThrowIfNull(instanceId);

        return Task.FromResult(_instances.ContainsKey(instanceId)
            ? Outcome.Success()
            : Outcome.Failure());
    }
}
