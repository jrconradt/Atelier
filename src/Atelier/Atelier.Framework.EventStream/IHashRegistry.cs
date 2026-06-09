using Atelier.Framework.Outcomes;

namespace Atelier.Framework.EventStream;

public interface IHashRegistry
{
        public Task<Outcome<string>> RegisterAsync(
        string hash,
        byte[] blob,
        CancellationToken cancellationToken = default);

        public Task<Outcome<byte[]?>> LookupAsync(
        string hash,
        CancellationToken cancellationToken = default);

        public Task<Outcome<bool>> ExistsAsync(
        string hash,
        CancellationToken cancellationToken = default);

        public Task<Outcome<Dictionary<string, byte[]>>> LookupBatchAsync(
        List<string> hashes,
        CancellationToken cancellationToken = default);

        public Task<Outcome<int>> GetReferenceCountAsync(
        string hash,
        CancellationToken cancellationToken = default);

        public Task<Outcome<int>> ReleaseAsync(
        string hash,
        CancellationToken cancellationToken = default);
}
