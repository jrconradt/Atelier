using Atelier.Framework.Infrastructure;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Infrastructure.Operation
{
        public abstract partial class OperationBase<TInput, TOutput> : IAtelier, IOperation<TInput, TOutput>
    {
                public abstract string OperationName { get; }

                public async Task<Outcome<TOutput>> TraverseAsync(
            TInput input,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteInternalAsync(input, context, cancellationToken).ConfigureAwait(false);
        }

                protected abstract Task<Outcome<TOutput>> ExecuteInternalAsync(
            TInput input,
            OperationContext context,
            CancellationToken cancellationToken);

                protected virtual void ValidateInput(TInput input)
        {
        }

                protected virtual void ValidateContext(OperationContext context)
        {
        }
    }

        public interface IOperation<TInput, TOutput>
    {
                public Task<Outcome<TOutput>> TraverseAsync(
            TInput input,
            OperationContext context,
            CancellationToken cancellationToken = default);
    }

        public class OperationContext
    {
                public string OperationId { get; set; } = Guid.NewGuid().ToString();

                public string CorrelationId { get; set; } = string.Empty;

                public string UserId { get; set; } = string.Empty;

                public DateTime StartedAt { get; set; } = DateTime.UtcNow;

                public Dictionary<string, object> Metadata { get; set; } = new();

    }
}
