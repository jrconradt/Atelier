using System.Collections.Concurrent;
using Atelier.Framework.Network.Hosts;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Network
{
    public class NetworkHostRegistry
    {
        private readonly NetworkHostDiscovery _discovery;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _serviceInstances = new();

        public NetworkHostRegistry(NetworkHostDiscovery discovery)
        {
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        }

        public Task<Outcome> RegisterServiceAsync(string serviceName, string instanceId, string endpoint, Dictionary<string, string>? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                return Task.FromResult(Outcome.Failure());
            }

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return Task.FromResult(Outcome.Failure());
            }

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return Task.FromResult(Outcome.Failure());
            }

            var record = new HostAnnouncementRecord
            {
                InstanceId = instanceId,
                ServiceName = serviceName,
                Endpoint = endpoint,
                Metadata = metadata ?? new Dictionary<string, string>(),
                State = HostState.Running
            };

            var announceOutcome = _discovery.Announce(record);
            if (!announceOutcome.IsSuccess)
            {
                return Task.FromResult(announceOutcome);
            }

            var instances = _serviceInstances.GetOrAdd(serviceName, _ => new ConcurrentDictionary<string, byte>());
            instances[instanceId] = 0;

            return Task.FromResult(Outcome.Success());
        }

        public Task<Outcome> UnregisterServiceAsync(string serviceName, string instanceId)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                return Task.FromResult(Outcome.Failure());
            }

            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return Task.FromResult(Outcome.Failure());
            }

            var revokeOutcome = _discovery.Revoke(instanceId);
            if (!revokeOutcome.IsSuccess)
            {
                return Task.FromResult(revokeOutcome);
            }

            if (_serviceInstances.TryGetValue(serviceName, out var instances))
            {
                instances.TryRemove(instanceId, out _);
                if (instances.IsEmpty)
                {
                    _serviceInstances.TryRemove(serviceName, out _);
                }
            }

            return Task.FromResult(Outcome.Success());
        }

        public Task<Outcome<List<NetworkHostResponse>>> DiscoverServicesAsync(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                return Task.FromResult(Outcome<List<NetworkHostResponse>>.Failure());
            }

            var announcementsOutcome = _discovery.GetAnnouncementsByServiceType(serviceName);
            if (!announcementsOutcome.IsSuccess)
            {
                return Task.FromResult(Outcome<List<NetworkHostResponse>>.Failure());
            }

            var responses = (announcementsOutcome.Data ?? Enumerable.Empty<HostAnnouncementRecord>())
                .Select(a => NetworkHostResponse.FromAnnouncement(a))
                .ToList();
            return Task.FromResult(Outcome<List<NetworkHostResponse>>.Success(responses));
        }

        public Task<Outcome<List<string>>> GetServiceNamesAsync()
        {
            return Task.FromResult(Outcome<List<string>>.Success(_serviceInstances.Keys.ToList()));
        }

        public Task<Outcome<List<string>>> GetServiceInstancesAsync(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                return Task.FromResult(Outcome<List<string>>.Failure());
            }

            if (_serviceInstances.TryGetValue(serviceName, out var instances))
            {
                return Task.FromResult(Outcome<List<string>>.Success(instances.Keys.ToList()));
            }

            return Task.FromResult(Outcome<List<string>>.Success(new List<string>()));
        }

        public Task<Outcome> UpdateServiceHealthAsync(string instanceId, HostState state)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return Task.FromResult(Outcome.Failure());
            }

            var announcementOutcome = _discovery.GetAnnouncementById(instanceId);
            if (!announcementOutcome.IsSuccess)
            {
                return Task.FromResult(Outcome.Failure());
            }

            var announcement = announcementOutcome.Data;
            if (announcement != null)
            {
                announcement.State = state;
                var announceOutcome = _discovery.Announce(announcement);
                if (!announceOutcome.IsSuccess)
                {
                    return Task.FromResult(announceOutcome);
                }
            }

            return Task.FromResult(Outcome.Success());
        }
    }
}
