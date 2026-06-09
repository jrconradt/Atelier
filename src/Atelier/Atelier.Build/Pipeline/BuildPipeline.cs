using System.Diagnostics;

namespace Atelier.Build.Pipeline;

public sealed class BuildPipeline
{
    private readonly BuildContext _context;
    private readonly BuildPresenter _presenter;
    private readonly PlatformProbe _platform;
    private readonly ShellRunner _shell;
    private readonly HookExecutor _hooks;

    public BuildPipeline(BuildContext context)
    {
        _context = context;
        _presenter = new BuildPresenter();
        _platform = new PlatformProbe();
        _shell = new ShellRunner(context);
        _hooks = new HookExecutor(context, _platform);
    }

    public async Task<BuildResult> TraverseAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await TraverseCoreAsync().ConfigureAwait(false);
        result = result with { Duration = stopwatch.Elapsed };

        if (result.IsSuccess && !_context.IsBoutiqueGeneration
            && !_context.IsSubsystemBuild
            && !_context.IsDirectProjectBuild)
        {
            _presenter.FullBuildSummary(result);
        }

        return result;
    }

    private async Task<BuildResult> TraverseCoreAsync()
    {
        var artifacts = new List<string>();
        var boutiques = new List<BoutiqueManifest>();

        try
        {
            _presenter.PipelineHeader();

            switch (_context.Mode)
            {
                case BuildMode.BoutiqueGeneration:
                {
                    var runner = new BoutiqueGenerationRunner(_context, _presenter);
                    return await runner.ExecuteAsync(artifacts).ConfigureAwait(false);
                }
                case BuildMode.Subsystem:
                {
                    var runner = new SubsystemBuildRunner(_context, _presenter, _shell, _platform, _hooks);
                    return await runner.ExecuteAsync(artifacts).ConfigureAwait(false);
                }
                case BuildMode.DirectProject:
                {
                    var runner = new DirectBuildRunner(_context);
                    return await runner.ExecuteAsync(artifacts).ConfigureAwait(false);
                }
                case BuildMode.FullBuild:
                {
                    var runner = new FullPipelineRunner(_context, _presenter, _shell);
                    return await runner.ExecuteAsync(artifacts, boutiques).ConfigureAwait(false);
                }
                default:
                {
                    throw new InvalidOperationException($"Unhandled build mode: {_context.Mode}");
                }
            }
        }
        catch (Exception ex)
        {
            _presenter.PipelineFailed(ex.Message);
            return BuildResult.Failure(ex.Message);
        }
    }
}
