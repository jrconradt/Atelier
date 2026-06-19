using System.Reflection;
using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Authorization;
using Atelier.Framework.Context;
using Atelier.Framework.Network.Enforcement;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Network;

public static class ScopeTierHeavyTests
{
    private const string TARGET = "global::Atelier.Framework.Network.Enforcement.ScopeRequirementResolver";
    private const string SUBJECT = "user-42";

    [ScopeResource(typeof(Scopes.Boutique))]
    private sealed class BoundService
    {
        [OperationEffect(EffectKind.Read)]
        public void GetBoutique()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void FetchBoutique()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void RetrieveBoutique()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void DiscoverBoutiques()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void FindBoutique()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void ListBoutiques()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void QueryBoutiques()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void SearchBoutiques()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void GetBoutiqueAsync()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void CreateBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void DeleteBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void UpdateBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void PublishBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void RevokeBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void CreateBoutiqueAsync()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void Reticulate()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void GetOrCreateBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void FindOrCreateBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void ListAndPurgeBoutiques()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void SearchAndReplaceBoutiques()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void RetrieveAndDeleteBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        [RequiresScope(Scopes.Boutique.WRITE)]
        public void UpdateBoutiqueExplicit()
        {
        }

        [OperationEffect(EffectKind.Read)]
        [RequiresScope(Scopes.Boutique.READ)]
        public void GetBoutiqueExplicitRead()
        {
        }
    }

    private sealed class UnboundService
    {
        public void GetBoutique()
        {
        }

        public void UpdateBoutique()
        {
        }
    }

    [ScopeResource(typeof(Scopes.Boutique))]
    private interface IBoundContract
    {
        [OperationEffect(EffectKind.Write)]
        void UpdateBoutique();

        [OperationEffect(EffectKind.Read)]
        void GetBoutique();
    }

    private sealed class BoundContractService : IBoundContract
    {
        [OperationEffect(EffectKind.Write)]
        public void UpdateBoutique()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void GetBoutique()
        {
        }
    }

    private interface IInnerContract
    {
        void UpdateBoutique();
    }

    [ScopeResource(typeof(Scopes.Boutique))]
    private interface IOuterContract : IInnerContract
    {
    }

    private sealed class DeepContractService : IOuterContract
    {
        [OperationEffect(EffectKind.Write)]
        public void UpdateBoutique()
        {
        }
    }

    private static MethodInfo Method<T>(string name)
    {
        return typeof(T).GetMethod(name) ?? throw new InvalidOperationException($"Method {name} not found");
    }

    private static AuthorizationContext Principal(string? userId,
                                                  params string[] scopes)
    {
        var auth = AuthorizationContext.Create(userId: userId, isVerified: true);
        foreach (var scope in scopes)
        {
            auth.AddPermission(scope);
        }

        return auth;
    }

    private static void IsTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static readonly string[] ReaderMethodNames = new[]
    {
        nameof(BoundService.GetBoutique),
        nameof(BoundService.FetchBoutique),
        nameof(BoundService.RetrieveBoutique),
        nameof(BoundService.DiscoverBoutiques),
        nameof(BoundService.FindBoutique),
        nameof(BoundService.ListBoutiques),
        nameof(BoundService.QueryBoutiques),
        nameof(BoundService.SearchBoutiques),
        nameof(BoundService.GetBoutiqueAsync)
    };

    private static readonly string[] MutatorMethodNames = new[]
    {
        nameof(BoundService.CreateBoutique),
        nameof(BoundService.DeleteBoutique),
        nameof(BoundService.UpdateBoutique),
        nameof(BoundService.PublishBoutique),
        nameof(BoundService.RevokeBoutique),
        nameof(BoundService.CreateBoutiqueAsync),
        nameof(BoundService.Reticulate)
    };

    [GeneratedTest("network.scope.tier.heavy.reader-derives-read-only", TARGET)]
    public static void EveryReaderOnBoundServiceDerivesReadOnly()
    {
        foreach (var name in ReaderMethodNames)
        {
            var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(name));

            IsTrue(required.Contains(Scopes.Boutique.READ), $"Reader {name} should derive the READ tier scope");
            IsTrue(!required.Contains(Scopes.Boutique.WRITE), $"Reader {name} should not derive the WRITE tier scope");
        }
    }

    [GeneratedTest("network.scope.tier.heavy.mutator-derives-write", TARGET)]
    public static void EveryMutatorOnBoundServiceDerivesWrite()
    {
        foreach (var name in MutatorMethodNames)
        {
            var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(name));

            IsTrue(required.Contains(Scopes.Boutique.WRITE), $"Mutator {name} should derive the WRITE tier scope");
        }
    }

    [GeneratedTest("network.scope.tier.heavy.reader-passes-with-read", TARGET)]
    public static void ReaderPassesWithReadScopeOnly()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(nameof(BoundService.FetchBoutique)));
        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A reader should pass with only the READ tier scope held");
    }

    [GeneratedTest("network.scope.tier.heavy.mutator-rejected-with-read-only", TARGET)]
    public static void MutatorRejectedWhenOnlyReadHeld()
    {
        foreach (var name in MutatorMethodNames)
        {
            var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(name));
            var auth = Principal(SUBJECT, Scopes.Boutique.READ);

            IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), $"Mutator {name} must be rejected when only the READ tier scope is held");
        }
    }

    [GeneratedTest("network.scope.tier.heavy.unbound-derives-nothing", TARGET)]
    public static void UnboundServiceDerivesNoTierScope()
    {
        var reader = ScopeRequirementResolver.ResolveRequiredScopes(Method<UnboundService>(nameof(UnboundService.GetBoutique)));
        var mutator = ScopeRequirementResolver.ResolveRequiredScopes(Method<UnboundService>(nameof(UnboundService.UpdateBoutique)));

        IsTrue(reader.Count == 0, "An unbound service reader should derive no scope");
        IsTrue(mutator.Count == 0, "An unbound service mutator should derive no scope");
    }

    [GeneratedTest("network.scope.tier.heavy.explicit-write-and-derived-dedupe", TARGET)]
    public static void ExplicitWriteAndDerivedWriteDedupe()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(nameof(BoundService.UpdateBoutiqueExplicit)));

        IsTrue(required.Count == 1, "Explicit WRITE plus derived WRITE on a mutator must collapse to one requirement");
        IsTrue(required.Contains(Scopes.Boutique.WRITE), "The single effective requirement should be the WRITE tier scope");
    }

    [GeneratedTest("network.scope.tier.heavy.explicit-read-and-derived-read-dedupe", TARGET)]
    public static void ExplicitReadAndDerivedReadDedupe()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(nameof(BoundService.GetBoutiqueExplicitRead)));

        IsTrue(required.Count == 1, "Explicit READ plus derived READ on a reader must collapse to one requirement");
        IsTrue(required.Contains(Scopes.Boutique.READ), "The single effective requirement should be the READ tier scope");
    }

    [GeneratedTest("network.scope.tier.heavy.interface-carried-mutator-write", TARGET)]
    public static void InterfaceCarriedResourceDerivesWriteForMutator()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundContractService>(nameof(BoundContractService.UpdateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "A ScopeResource carried on an implemented interface should derive WRITE for a mutating operation");
    }

    [GeneratedTest("network.scope.tier.heavy.interface-carried-reader-read", TARGET)]
    public static void InterfaceCarriedResourceDerivesReadForReader()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundContractService>(nameof(BoundContractService.GetBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.READ), "A ScopeResource carried on an implemented interface should derive READ for a reader operation");
        IsTrue(!required.Contains(Scopes.Boutique.WRITE), "A reader should not derive WRITE even via interface-carried resource");
    }

    [GeneratedTest("network.scope.tier.heavy.deep-interface-carried-mutator-write", TARGET)]
    public static void DeepInterfaceChainCarriesResourceForMutator()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<DeepContractService>(nameof(DeepContractService.UpdateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "A ScopeResource on an outer interface in an inheritance chain should derive WRITE for a mutating operation");
    }

    [GeneratedTest("network.scope.tier.heavy.getorcreate-must-require-write", TARGET)]
    public static void GetOrCreateMustRequireWriteScope()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(nameof(BoundService.GetOrCreateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "GetOrCreate performs a state change and must require the WRITE tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "GetOrCreate must be rejected for a principal holding only READ");
    }

    [GeneratedTest("network.scope.tier.heavy.findorcreate-must-require-write", TARGET)]
    public static void FindOrCreateMustRequireWriteScope()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(nameof(BoundService.FindOrCreateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "FindOrCreate performs a state change and must require the WRITE tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "FindOrCreate must be rejected for a principal holding only READ");
    }

    [GeneratedTest("network.scope.tier.heavy.listandpurge-must-require-write", TARGET)]
    public static void ListAndPurgeMustRequireWriteScope()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(nameof(BoundService.ListAndPurgeBoutiques)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "ListAndPurge performs a state change and must require the WRITE tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "ListAndPurge must be rejected for a principal holding only READ");
    }

    [GeneratedTest("network.scope.tier.heavy.searchandreplace-must-require-write", TARGET)]
    public static void SearchAndReplaceMustRequireWriteScope()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(nameof(BoundService.SearchAndReplaceBoutiques)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "SearchAndReplace performs a state change and must require the WRITE tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "SearchAndReplace must be rejected for a principal holding only READ");
    }

    [GeneratedTest("network.scope.tier.heavy.retrieveanddelete-must-require-write", TARGET)]
    public static void RetrieveAndDeleteMustRequireWriteScope()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BoundService>(nameof(BoundService.RetrieveAndDeleteBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "RetrieveAndDelete performs a state change and must require the WRITE tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "RetrieveAndDelete must be rejected for a principal holding only READ");
    }
}
