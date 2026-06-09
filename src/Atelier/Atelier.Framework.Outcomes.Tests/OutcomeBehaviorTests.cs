using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Outcomes.Tests;

public static class OutcomeBehaviorTests
{
    [GeneratedTest("Outcomes/Outcome-Success-Reports-Success", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void SuccessReportsSuccess()
    {
        var outcome = Outcome.Success();

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("Outcome.Success() reported IsSuccess == false");
        }
        if (outcome.IsFailure())
        {
            throw new InvalidOperationException("IsFailure() returned true for a success");
        }
    }

    [GeneratedTest("Outcomes/Outcome-Failure-Reports-Failure", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void FailureReportsFailure()
    {
        var outcome = Outcome.Failure();

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Outcome.Failure() reported IsSuccess == true");
        }
        if (!outcome.IsFailure())
        {
            throw new InvalidOperationException("IsFailure() returned false for a failure");
        }
    }

    [GeneratedTest("Outcomes/Outcome-Implicit-From-True-Is-Success", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void ImplicitFromTrueProducesSuccess()
    {
        Outcome outcome = true;

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("implicit Outcome from true was not a success");
        }
    }

    [GeneratedTest("Outcomes/Outcome-Implicit-From-False-Is-Failure", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void ImplicitFromFalseProducesFailure()
    {
        Outcome outcome = false;

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("implicit Outcome from false was a success");
        }
    }

    [GeneratedTest("Outcomes/Outcome-Success-Factory-Is-Success", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void SuccessFactoryProducesSuccess()
    {
        var outcome = Outcome.Success();

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("Outcome.Success() did not produce a success");
        }
    }

    [GeneratedTest("Outcomes/Outcome-Failure-Factory-Is-Failure", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void FailureFactoryProducesFailure()
    {
        var outcome = Outcome.Failure();

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Outcome.Failure() produced a success");
        }
    }

    [GeneratedTest("Outcomes/Outcome-Equality-Distinguishes-States", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void EqualityDistinguishesSuccessFromFailure()
    {
        var a = Outcome.Success();
        var b = Outcome.Success();
        var failure = Outcome.Failure();

        if (!(a == b))
        {
            throw new InvalidOperationException("two successes compared unequal via ==");
        }
        if (a != b)
        {
            throw new InvalidOperationException("two successes compared unequal via !=");
        }
        if (a == failure)
        {
            throw new InvalidOperationException("a success compared equal to a failure");
        }
        if (!a.Equals((object)b))
        {
            throw new InvalidOperationException("Equals(object) returned false for equal successes");
        }
        if (a.Equals("not an outcome"))
        {
            throw new InvalidOperationException("Equals(object) returned true for a non-Outcome value");
        }
    }

    [GeneratedTest("Outcomes/Outcome-GetHashCode-Stable-For-Equal-Values", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void GetHashCodeAgreesForEqualValues()
    {
        var a = Outcome.Success();
        var b = Outcome.Success();

        if (a.GetHashCode() != b.GetHashCode())
        {
            throw new InvalidOperationException("equal Outcomes produced different hash codes");
        }
    }

    [GeneratedTest("Outcomes/Outcome-Match-Selects-Branch", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void MatchSelectsTheBranchForTheState()
    {
        var successBranch = Outcome.Success().Match(
            () => "ok",
            () => "fail");
        if (successBranch != "ok")
        {
            throw new InvalidOperationException($"Match on success returned '{successBranch}', expected 'ok'");
        }

        var failureBranch = Outcome.Failure().Match(
            () => "ok",
            () => "fail");
        if (failureBranch != "fail")
        {
            throw new InvalidOperationException($"Match on failure returned '{failureBranch}', expected 'fail'");
        }
    }

    [GeneratedTest("Outcomes/Outcome-OnFailure-Runs-Only-On-Failure", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void OnFailureRunsOnlyForFailures()
    {
        var ran = 0;

        Outcome.Success().OnFailure(() => ran++);
        if (ran != 0)
        {
            throw new InvalidOperationException("OnFailure ran for a success");
        }

        Outcome.Failure().OnFailure(() => ran++);
        if (ran != 1)
        {
            throw new InvalidOperationException($"OnFailure ran {ran} times for one failure");
        }
    }

    [GeneratedTest("Outcomes/Bool-ToOutcome-Maps-State", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void BoolToOutcomeMapsTrueToSuccessAndFalseToFailure()
    {
        if (!true.ToOutcome().IsSuccess)
        {
            throw new InvalidOperationException("true.ToOutcome() was not a success");
        }

        if (false.ToOutcome().IsSuccess)
        {
            throw new InvalidOperationException("false.ToOutcome() was a success");
        }
    }

    [GeneratedTest("Outcomes/Exception-ToOutcome-Is-Failure", "global::Atelier.Framework.Outcomes.Outcome")]
    public static void ExceptionToOutcomeProducesFailure()
    {
        var outcome = new InvalidOperationException("nope").ToOutcome();

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Exception.ToOutcome() was a success");
        }
    }
}
