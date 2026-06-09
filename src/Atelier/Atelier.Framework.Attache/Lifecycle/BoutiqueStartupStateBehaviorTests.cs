using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Attache.Lifecycle;

public static class BoutiqueStartupStateBehaviorTests
{
    [GeneratedTest("Attache/Startup-State-Begins-Unstarted-And-Not-Ready", "global::Atelier.Framework.Attache.Lifecycle.BoutiqueStartupState")]
    public static void FreshStateIsUnstartedNotReadyAndNotDraining()
    {
        var state = new BoutiqueStartupState();

        if (state.HasStarted)
        {
            throw new InvalidOperationException("fresh state reported HasStarted before any result");
        }
        if (state.IsReady)
        {
            throw new InvalidOperationException("fresh state reported IsReady before any result");
        }
        if (state.IsDraining)
        {
            throw new InvalidOperationException("fresh state reported IsDraining before BeginDraining");
        }
        if (state.Result is not null)
        {
            throw new InvalidOperationException("fresh state exposed a non-null Result");
        }
    }

    [GeneratedTest("Attache/Startup-Success-Result-Becomes-Started-And-Ready", "global::Atelier.Framework.Attache.Lifecycle.BoutiqueStartupState")]
    public static void SuccessResultMarksStartedAndReady()
    {
        var state = new BoutiqueStartupState();

        state.SetResult(Outcome.Success());

        if (!state.HasStarted)
        {
            throw new InvalidOperationException("state did not report HasStarted after a result was set");
        }
        if (!state.IsReady)
        {
            throw new InvalidOperationException("successful startup did not report IsReady");
        }
        if (state.Result is not { IsSuccess: true })
        {
            throw new InvalidOperationException("stored Result was not the successful outcome");
        }
    }

    [GeneratedTest("Attache/Startup-Failure-Result-Is-Started-But-Not-Ready", "global::Atelier.Framework.Attache.Lifecycle.BoutiqueStartupState")]
    public static void FailureResultMarksStartedButNotReady()
    {
        var state = new BoutiqueStartupState();

        state.SetResult(Outcome.Failure());

        if (!state.HasStarted)
        {
            throw new InvalidOperationException("failed startup should still report HasStarted");
        }
        if (state.IsReady)
        {
            throw new InvalidOperationException("failed startup must not report IsReady");
        }
        if (state.Result is not { IsSuccess: false })
        {
            throw new InvalidOperationException("stored Result was not the failed outcome");
        }
    }

    [GeneratedTest("Attache/Startup-Draining-Forces-Not-Ready", "global::Atelier.Framework.Attache.Lifecycle.BoutiqueStartupState")]
    public static void DrainingClearsReadinessEvenAfterSuccessfulStart()
    {
        var state = new BoutiqueStartupState();
        state.SetResult(Outcome.Success());

        state.BeginDraining();

        if (!state.IsDraining)
        {
            throw new InvalidOperationException("BeginDraining did not flip IsDraining");
        }
        if (state.IsReady)
        {
            throw new InvalidOperationException("a draining state must not report IsReady");
        }
        if (!state.HasStarted)
        {
            throw new InvalidOperationException("draining must not erase HasStarted");
        }
    }
}
