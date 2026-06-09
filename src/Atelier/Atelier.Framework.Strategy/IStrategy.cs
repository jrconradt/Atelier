using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Strategy
{
        public interface IAsyncStrategy<TContext, TResult>
    {
        public Task<Outcome<TResult>> TraverseAsync(TContext context, CancellationToken cancellationToken = default);
    }

        public interface IAsyncStrategy<TContext>
    {
        public Task TraverseAsync(TContext context, CancellationToken cancellationToken = default);
    }
}
