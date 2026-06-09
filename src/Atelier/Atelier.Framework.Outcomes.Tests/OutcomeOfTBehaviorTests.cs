using Atelier.Framework.Outcomes;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Outcomes.Tests;

public static class OutcomeOfTBehaviorTests
{
    [GeneratedTest("Outcomes/OutcomeT-Success-Carries-Data", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void SuccessCarriesDataAndReportsSuccess()
    {
        var outcome = Outcome<int>.Success(42);

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("Outcome<int>.Success(42) reported IsSuccess == false");
        }
        if (outcome.Data != 42)
        {
            throw new InvalidOperationException($"Data was {outcome.Data}, expected 42");
        }
        if (outcome.Value() != 42)
        {
            throw new InvalidOperationException($"Value() returned {outcome.Value()}, expected 42");
        }
        if (outcome.IsFailure())
        {
            throw new InvalidOperationException("IsFailure() returned true for a success");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Failure-Reports-Failure-And-Default-Data", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void FailureReportsFailureAndDefaultData()
    {
        var outcome = Outcome<int>.Failure();

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Outcome<int>.Failure() reported IsSuccess == true");
        }
        if (!outcome.IsFailure())
        {
            throw new InvalidOperationException("IsFailure() returned false for a failure");
        }
        if (outcome.Data != 0)
        {
            throw new InvalidOperationException($"failure exposed Data {outcome.Data}, expected default 0");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Null-Data-Ctor-Throws", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void SuccessCtorRejectsNullData()
    {
        var threw = false;
        try
        {
            _ = new Outcome<string>((string)null!);
        }
        catch (ArgumentNullException)
        {
            threw = true;
        }

        if (!threw)
        {
            throw new InvalidOperationException("Outcome<string>(null) did not throw ArgumentNullException");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Default-Is-Default-Constructed-Is-Not", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void IsDefaultDistinguishesDefaultFromConstructed()
    {
        var uninitialized = default(Outcome<int>);
        if (!uninitialized.IsDefault)
        {
            throw new InvalidOperationException("default(Outcome<int>) reported IsDefault == false");
        }

        var success = Outcome<int>.Success(1);
        if (success.IsDefault)
        {
            throw new InvalidOperationException("constructed success reported IsDefault == true");
        }

        var failure = Outcome<int>.Failure();
        if (failure.IsDefault)
        {
            throw new InvalidOperationException("constructed failure reported IsDefault == true");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Implicit-From-Data-Is-Success", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void ImplicitFromDataProducesSuccess()
    {
        Outcome<string> outcome = "payload";

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("implicit Outcome<string> from data was not a success");
        }
        if (outcome.Data != "payload")
        {
            throw new InvalidOperationException($"implicit success Data was '{outcome.Data}'");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Success-Factory-Carries-Data", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void SuccessFactoryCarriesData()
    {
        var outcome = Outcome<string>.Success("payload");

        if (!outcome.IsSuccess)
        {
            throw new InvalidOperationException("Outcome<string>.Success did not produce a success");
        }
        if (outcome.Data != "payload")
        {
            throw new InvalidOperationException($"success carried Data '{outcome.Data}'");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Failure-Factory-Is-Failure", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void FailureFactoryProducesFailure()
    {
        var outcome = Outcome<string>.Failure();

        if (outcome.IsSuccess)
        {
            throw new InvalidOperationException("Outcome<string>.Failure() produced a success");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Equality-Compares-Data-And-State", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void EqualityComparesDataAndErrorState()
    {
        var a = Outcome<int>.Success(5);
        var b = Outcome<int>.Success(5);
        var differentData = Outcome<int>.Success(6);
        var failure = Outcome<int>.Failure();

        if (!(a == b))
        {
            throw new InvalidOperationException("two equal successes compared unequal via ==");
        }
        if (a != b)
        {
            throw new InvalidOperationException("two equal successes compared unequal via !=");
        }
        if (a == differentData)
        {
            throw new InvalidOperationException("successes with different Data compared equal");
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

    [GeneratedTest("Outcomes/OutcomeT-GetHashCode-Stable-For-Equal-Values", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void GetHashCodeAgreesForEqualValues()
    {
        var a = Outcome<int>.Success(5);
        var b = Outcome<int>.Success(5);

        if (a.GetHashCode() != b.GetHashCode())
        {
            throw new InvalidOperationException("equal Outcome<int> values produced different hash codes");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Match-Selects-Branch", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void MatchSelectsTheBranchForTheState()
    {
        var successBranch = Outcome<int>.Success(3).Match(
            data => $"ok:{data}",
            () => "fail");
        if (successBranch != "ok:3")
        {
            throw new InvalidOperationException($"Match on success returned '{successBranch}'");
        }

        var failureBranch = Outcome<int>.Failure().Match(
            data => $"ok:{data}",
            () => "fail");
        if (failureBranch != "fail")
        {
            throw new InvalidOperationException($"Match on failure returned '{failureBranch}'");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Bind-Chains-Success-And-Short-Circuits-Failure", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void BindChainsOnSuccessAndShortCircuitsOnFailure()
    {
        var chained = Outcome<int>.Success(4).Bind(value => Outcome<string>.Success($"v{value}"));
        if (!chained.IsSuccess
            || chained.Data != "v4")
        {
            throw new InvalidOperationException($"Bind on success produced success={chained.IsSuccess} data='{chained.Data}'");
        }

        var ran = false;
        var shortCircuited = Outcome<int>.Failure().Bind(value =>
        {
            ran = true;
            return Outcome<string>.Success("unreachable");
        });
        if (ran)
        {
            throw new InvalidOperationException("Bind invoked the continuation for a failure");
        }
        if (shortCircuited.IsSuccess)
        {
            throw new InvalidOperationException("Bind on a failure produced a success");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Map-Transforms-Success-And-Tunnels-Failure", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void MapTransformsSuccessAndTunnelsFailure()
    {
        var mapped = Outcome<int>.Success(8).Map(value => value * 2);
        if (!mapped.IsSuccess
            || mapped.Data != 16)
        {
            throw new InvalidOperationException($"Map on success produced success={mapped.IsSuccess} data={mapped.Data}");
        }

        var tunneled = Outcome<int>.Failure().Map(value => value * 2);
        if (tunneled.IsSuccess)
        {
            throw new InvalidOperationException("Map on a failure produced a success");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-Tap-Runs-Only-On-Success", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void TapRunsOnlyForSuccessAndReturnsSource()
    {
        var seen = 0;

        var passedThrough = Outcome<int>.Success(11).Tap(value => seen = value);
        if (seen != 11)
        {
            throw new InvalidOperationException($"Tap on success observed {seen}, expected 11");
        }
        if (!passedThrough.IsSuccess
            || passedThrough.Data != 11)
        {
            throw new InvalidOperationException("Tap did not return the source outcome unchanged");
        }

        seen = -1;
        Outcome<int>.Failure().Tap(value => seen = value);
        if (seen != -1)
        {
            throw new InvalidOperationException("Tap ran the action for a failure");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-OnFailure-Runs-Only-On-Failure", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void OnFailureRunsOnlyForFailures()
    {
        var ran = 0;

        Outcome<int>.Success(1).OnFailure(() => ran++);
        if (ran != 0)
        {
            throw new InvalidOperationException("OnFailure ran for a success");
        }

        Outcome<int>.Failure().OnFailure(() => ran++);
        if (ran != 1)
        {
            throw new InvalidOperationException($"OnFailure ran {ran} times for one failure");
        }
    }

    [GeneratedTest("Outcomes/OutcomeT-TunnelFailure-Stays-Failure-Across-Type", "global::Atelier.Framework.Outcomes.Outcome`1")]
    public static void TunnelFailureStaysFailureAcrossType()
    {
        var tunneled = Outcome<int>.Failure().TunnelFailure<int, string>();

        if (tunneled.IsSuccess)
        {
            throw new InvalidOperationException("TunnelFailure produced a success");
        }
    }
}
