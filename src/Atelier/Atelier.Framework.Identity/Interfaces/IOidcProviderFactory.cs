using Atelier.Framework.Outcomes;
using Atelier.Framework.Attributes;

namespace Atelier.Framework.Identity.Interfaces;

public interface IOidcProviderFactory
{
    Task<Outcome<IOidcProvider>> GetProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default);

    Task<Outcome<IOidcProvider>> GetDefaultProviderAsync(
        CancellationToken cancellationToken = default);

    Task<Outcome<IEnumerable<IOidcProvider>>> GetAllProvidersAsync(
        CancellationToken cancellationToken = default);

    Task<Outcome> IsProviderAvailableAsync(
        string providerName,
        CancellationToken cancellationToken = default);

    Task<Outcome> ResetProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default);

    Task<Outcome> ResetAllProvidersAsync(
        CancellationToken cancellationToken = default);
}