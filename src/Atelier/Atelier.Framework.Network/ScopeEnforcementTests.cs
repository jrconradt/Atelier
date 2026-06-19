using System.Reflection;
using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Authorization;
using Atelier.Framework.Context;
using Atelier.Framework.Network.Enforcement;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Network;

public static class ScopeEnforcementTests
{
    private const string TARGET = "global::Atelier.Framework.Network.Enforcement.ScopeRequirementResolver";
    private const string SUBJECT = "user-42";
    private const string OTHER = "user-99";

    private sealed class SingleScopeService
    {
        [RequiresScope(Scopes.Boutique.READ)]
        public void Read(string identityId)
        {
            ArgumentNullException.ThrowIfNull(identityId);
        }
    }

    [RequiresScope(Scopes.Boutique.READ)]
    private sealed class DuplicateScopeService
    {
        [RequiresScope(Scopes.Boutique.READ)]
        public void Read()
        {
        }
    }

    private sealed class SelfService
    {
        [AllowSelf]
        public void Profile(string identityId)
        {
            ArgumentNullException.ThrowIfNull(identityId);
        }
    }

    [ScopeResource(typeof(Scopes.Boutique))]
    private sealed class TieredService
    {
        [OperationEffect(EffectKind.Read)]
        public void GetBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void UpdateBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void Reticulate()
        {
        }

        [OperationEffect(EffectKind.Write)]
        [RequiresScope(Scopes.Boutique.WRITE)]
        public void UpdateBoutiqueExplicit()
        {
        }
    }

    [ScopeResource(typeof(Scopes.Boutique))]
    private interface ITieredContract
    {
        [OperationEffect(EffectKind.Write)]
        void UpdateBoutique();
    }

    private sealed class TieredContractService : ITieredContract
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

    [GeneratedTest("network.scope.required.present-passes", TARGET)]
    public static void PrincipalWithRequiredScopePasses()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding the required scope should be authorized");
    }

    [GeneratedTest("network.scope.required.missing-rejected", TARGET)]
    public static void PrincipalMissingRequiredScopeRejected()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, Scopes.Boutique.WRITE);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal lacking the required scope should be rejected");
    }

    [GeneratedTest("network.scope.allowself.matching-subject-passes", TARGET)]
    public static void AllowSelfPermitsMatchingSubject()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = Principal(SUBJECT);

        IsTrue(ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName), "AllowSelf metadata should resolve");

        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf should permit the matching subject");
    }

    [GeneratedTest("network.scope.allowself.different-subject-rejected", TARGET)]
    public static void AllowSelfRejectsDifferentSubject()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { OTHER }, parameterName);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf should reject a non-matching subject");
    }

    [GeneratedTest("network.scope.required.duplicate-idempotent", TARGET)]
    public static void DuplicateScopeDeclarationsCollapseToOne()
    {
        var single = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var duplicated = ScopeRequirementResolver.ResolveRequiredScopes(Method<DuplicateScopeService>(nameof(DuplicateScopeService.Read)));

        IsTrue(duplicated.Count == 1, "Class plus method declaration of the same scope should collapse to one effective requirement");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, single) == ScopeAuthorizationEvaluator.IsAuthorized(auth, duplicated), "Duplicate scope declarations must behave identically to a single one");
    }

    [GeneratedTest("network.scope.tier.mutator-requires-write-present-passes", TARGET)]
    public static void MutatorOnBoundServiceRequiresWriteScopePresentPasses()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.UpdateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "Mutating operation on a bound service should derive the WRITE tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.WRITE);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding the WRITE tier scope should pass a mutating operation");
    }

    [GeneratedTest("network.scope.tier.mutator-requires-write-missing-rejected", TARGET)]
    public static void MutatorOnBoundServiceRequiresWriteScopeMissingRejected()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.UpdateBoutique)));
        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding only the READ tier scope should be rejected on a mutating operation");
    }

    [GeneratedTest("network.scope.tier.reader-requires-read-only", TARGET)]
    public static void ReaderOnBoundServiceRequiresReadScopeOnly()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.GetBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.READ), "Reader operation on a bound service should derive the READ tier scope");
        IsTrue(!required.Contains(Scopes.Boutique.WRITE), "Reader operation should not derive the WRITE tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding the READ tier scope should pass a reader operation");
    }

    [GeneratedTest("network.scope.tier.non-verb-name-write-effect", TARGET)]
    public static void NonVerbNameWithWriteEffectDerivesWrite()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.Reticulate)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "A method whose name is not a recognizable verb still resolves the WRITE tier from its declared Write effect");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding only the READ tier scope should be rejected on a Write-effect operation");
    }

    [GeneratedTest("network.scope.tier.explicit-and-derived-dedupe", TARGET)]
    public static void ExplicitAndDerivedTierScopeDedupe()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.UpdateBoutiqueExplicit)));

        IsTrue(required.Count == 1, "An explicit RequiresScope and the derived tier scope of the same value must collapse to one requirement");
        IsTrue(required.Contains(Scopes.Boutique.WRITE), "The single effective requirement should be the WRITE tier scope");
    }

    [GeneratedTest("network.scope.tier.contract-carried-resource-binds", TARGET)]
    public static void ContractCarriedScopeResourceBindsTierScope()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredContractService>(nameof(TieredContractService.UpdateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "A ScopeResource carried on an implemented interface should derive the WRITE tier scope for a mutating operation");
    }
}
