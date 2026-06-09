
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.StateMachine.Service
{
    public interface IStateMachineRegistry
    {
        public Task<Outcome> Register(string instanceId, IStateMachineInstance instance);
        public Task<Outcome> Unregister(string instanceId);
        public Task<Outcome<IStateMachineInstance>> GetInstance(string instanceId);
        public Task<Outcome<IEnumerable<IStateMachineInstance>>> GetAllInstances();
        public Task<Outcome<IEnumerable<IStateMachineInstance>>> GetInstancesByType<T>() where T : class;
        public Task<Outcome<IEnumerable<IStateMachineInstance>>> GetInstancesByTag(string tag, string value);
        public Task<Outcome> IsRegistered(string instanceId);
        public int Count { get; }
    }
}
