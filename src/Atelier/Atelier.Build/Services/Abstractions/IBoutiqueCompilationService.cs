using Atelier.Build.Discovery;
using Atelier.Build.Pipeline;

namespace Atelier.Build.Services.Abstractions;

public interface IBoutiqueCompilationService
{
        public Task<BoutiqueManifest> CompileBoutiqueAsync(
        BoutiqueDefinition definition,
        string outputDirectory,
        CancellationToken cancellationToken = default);

        public Task<bool> CompileSolutionAsync(
        string solutionPath,
        string outputDirectory,
        string configuration = "Debug",
        CancellationToken cancellationToken = default);

        public Task<bool> CompileProjectAsync(
        string projectPath,
        string outputDirectory,
        string configuration = "Debug",
        CancellationToken cancellationToken = default);

        public void ResetBuildCache();
}
