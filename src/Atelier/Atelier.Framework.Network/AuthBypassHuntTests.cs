using System.Reflection;
using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Authorization;
using Atelier.Framework.Context;
using Atelier.Framework.Network.Enforcement;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Network;

public static class AuthBypassHuntTests
{
    private const string TARGET = "global::Atelier.Framework.Network.Enforcement.ScopeRequirementResolver";
    private const string SUBJECT = "user-42";
    private const string OTHER = "user-99";

    public static class BlankWriteResource
    {
        public const string READ = "atelier.blank.read";
        public const string WRITE = "";
    }

    [ScopeResource(typeof(Scopes.Boutique))]
    private sealed class NameInferenceService
    {
        [OperationEffect(EffectKind.Write)]
        public void GetOrCreateBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void FindOrCreateBoutique()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void ListAndArchiveBoutiques()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void SearchAndDeleteBoutiques()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void QueryThenPurge()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void RetrieveAndReset()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void DiscoverOrProvision()
        {
        }

        [OperationEffect(EffectKind.Write)]
        public void GetOrCreateBoutiqueAsync()
        {
        }

        [OperationEffect(EffectKind.Read)]
        public void ListBoutiquesReadOnly()
        {
        }

        [RequiresScope(Scopes.Boutique.READ)]
        public void UndeclaredEffectExplicitScope()
        {
        }
    }

    [ScopeResource(typeof(BlankWriteResource))]
    private sealed class BlankWriteService
    {
        [OperationEffect(EffectKind.Write)]
        public void UpdateBoutique()
        {
        }
    }

    [RequiresScope(Scopes.Boutique.READ)]
    [RequiresScope(Scopes.Boutique.WRITE)]
    private sealed class TwoDistinctScopeService
    {
        public void Operate()
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

        [AllowSelf("userId")]
        public void Mismatch(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
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

    [GeneratedTest("network.scope.bypass.getorcreate-requires-write", TARGET)]
    public static void GetOrCreateMustRequireWriteTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.GetOrCreateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "GetOrCreate declares the Write effect and must resolve the WRITE tier scope despite its reader-shaped name");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding only READ must not be authorized to call GetOrCreate");
    }

    [GeneratedTest("network.scope.bypass.findorcreate-requires-write", TARGET)]
    public static void FindOrCreateMustRequireWriteTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.FindOrCreateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "FindOrCreate declares the Write effect and must resolve the WRITE tier scope despite its reader-shaped name");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding only READ must not be authorized to call FindOrCreate");
    }

    [GeneratedTest("network.scope.bypass.listandarchive-requires-write", TARGET)]
    public static void ListAndArchiveMustRequireWriteTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.ListAndArchiveBoutiques)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "ListAndArchive declares the Write effect and must resolve the WRITE tier scope despite its reader-shaped name");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding only READ must not be authorized to call ListAndArchive");
    }

    [GeneratedTest("network.scope.bypass.searchanddelete-requires-write", TARGET)]
    public static void SearchAndDeleteMustRequireWriteTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.SearchAndDeleteBoutiques)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "SearchAndDelete declares the Write effect and must resolve the WRITE tier scope despite its reader-shaped name");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding only READ must not be authorized to call SearchAndDelete");
    }

    [GeneratedTest("network.scope.bypass.querythenpurge-requires-write", TARGET)]
    public static void QueryThenPurgeMustRequireWriteTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.QueryThenPurge)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "QueryThenPurge declares the Write effect and must resolve the WRITE tier scope despite its reader-shaped name");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding only READ must not be authorized to call QueryThenPurge");
    }

    [GeneratedTest("network.scope.bypass.retrieveandreset-requires-write", TARGET)]
    public static void RetrieveAndResetMustRequireWriteTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.RetrieveAndReset)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "RetrieveAndReset declares the Write effect and must resolve the WRITE tier scope despite its reader-shaped name");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding only READ must not be authorized to call RetrieveAndReset");
    }

    [GeneratedTest("network.scope.bypass.discoverorprovision-requires-write", TARGET)]
    public static void DiscoverOrProvisionMustRequireWriteTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.DiscoverOrProvision)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "DiscoverOrProvision declares the Write effect and must resolve the WRITE tier scope despite its reader-shaped name");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding only READ must not be authorized to call DiscoverOrProvision");
    }

    [GeneratedTest("network.scope.bypass.getorcreate-async-requires-write", TARGET)]
    public static void GetOrCreateAsyncMustRequireWriteTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.GetOrCreateBoutiqueAsync)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "GetOrCreateAsync declares the Write effect and must resolve the WRITE tier scope despite its reader-shaped name");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding only READ must not be authorized to call GetOrCreateAsync");
    }

    [GeneratedTest("network.scope.bypass.declared-read-resolves-read-tier", TARGET)]
    public static void DeclaredReadEffectResolvesReadTier()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.ListBoutiquesReadOnly)));

        IsTrue(required.Contains(Scopes.Boutique.READ), "A method declaring the Read effect must resolve the READ tier scope");
        IsTrue(!required.Contains(Scopes.Boutique.WRITE), "A Read-effect method must not resolve the WRITE tier scope");

        var readerOnly = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(readerOnly, required), "A principal holding READ must be authorized to call a Read-effect operation");
    }

    [GeneratedTest("network.scope.bypass.undeclared-effect-fails-closed", TARGET)]
    public static void UndeclaredOperationEffectFailsClosed()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NameInferenceService>(nameof(NameInferenceService.UndeclaredEffectExplicitScope)));

        IsTrue(required.FailClosed, "An operation on a [ScopeResource] type that declares no [OperationEffect] must fail closed: the tier is not derivable");

        var holdsRead = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(holdsRead, required), "A fail-closed requirement must deny even a principal holding the explicitly named scope");
    }

    [GeneratedTest("network.scope.bypass.blank-write-field-not-admitted", TARGET)]
    public static void MutatorOnResourceWithBlankWriteFieldMustNotBeAdmitted()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<BlankWriteService>(nameof(BlankWriteService.UpdateBoutique)));

        var anyPrincipal = Principal(SUBJECT, Scopes.Boutique.READ, Scopes.Boutique.WRITE);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(anyPrincipal, required), "A mutating operation whose derived WRITE scope is the empty string must not admit a principal that merely lacks an empty-string permission key");
    }

    [GeneratedTest("network.scope.bypass.two-distinct-scopes-both-enforced", TARGET)]
    public static void TwoDistinctRequiredScopesAreBothEnforced()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TwoDistinctScopeService>(nameof(TwoDistinctScopeService.Operate)));

        IsTrue(required.Count == 2, "Two genuinely different required scopes must not collapse to one");

        var holdsReadOnly = Principal(SUBJECT, Scopes.Boutique.READ);
        var holdsWriteOnly = Principal(SUBJECT, Scopes.Boutique.WRITE);
        var holdsBoth = Principal(SUBJECT, Scopes.Boutique.READ, Scopes.Boutique.WRITE);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(holdsReadOnly, required), "Holding only READ must be rejected when both READ and WRITE are required");
        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(holdsWriteOnly, required), "Holding only WRITE must be rejected when both READ and WRITE are required");
        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(holdsBoth, required), "Holding both READ and WRITE must be authorized when both are required");
    }

    [GeneratedTest("network.scope.bypass.case-mismatch-permission-denied", TARGET)]
    public static void UpperCasePermissionKeyMustNotSatisfyLowerCaseRequirement()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = Principal(SUBJECT, "ATELIER.BOUTIQUE.READ");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "An upper-cased permission key must not satisfy a lower-cased required scope");
    }

    [GeneratedTest("network.scope.bypass.trailing-whitespace-permission-denied", TARGET)]
    public static void TrailingWhitespacePermissionKeyMustNotSatisfyRequirement()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = Principal(SUBJECT, Scopes.Boutique.READ + " ");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A trailing-whitespace permission key must not satisfy the exact required scope");
    }

    [GeneratedTest("network.scope.bypass.leading-whitespace-permission-denied", TARGET)]
    public static void LeadingWhitespacePermissionKeyMustNotSatisfyRequirement()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = Principal(SUBJECT, " " + Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A leading-whitespace permission key must not satisfy the exact required scope");
    }

    [GeneratedTest("network.scope.bypass.unicode-lookalike-permission-denied", TARGET)]
    public static void UnicodeLookalikePermissionKeyMustNotSatisfyRequirement()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = Principal(SUBJECT, "atelier.boutique.reаd");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A Cyrillic-lookalike permission key must not satisfy the ASCII required scope");
    }

    [GeneratedTest("network.scope.bypass.fullwidth-lookalike-permission-denied", TARGET)]
    public static void FullWidthLookalikePermissionKeyMustNotSatisfyRequirement()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = Principal(SUBJECT, "ａtelier.boutique.read");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A full-width lookalike permission key must not satisfy the ASCII required scope");
    }

    [GeneratedTest("network.scope.bypass.wildcard-permission-denied", TARGET)]
    public static void WildcardPermissionKeyMustNotSatisfyExactRequirement()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = Principal(SUBJECT, "atelier.boutique.*");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A wildcard permission key must not grant an exact required scope");
    }

    [GeneratedTest("network.scope.bypass.prefix-permission-denied", TARGET)]
    public static void PrefixPermissionKeyMustNotSatisfyExactRequirement()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = Principal(SUBJECT, "atelier.boutique");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A scope prefix must not grant the more specific exact required scope");
    }

    [GeneratedTest("network.scope.bypass.unverified-context-denied", TARGET)]
    public static void UnverifiedContextWithCorrectScopeMustBeDenied()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: false);
        auth.AddPermission(Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "An unverified context must be denied even when it carries the required scope");
    }

    [GeneratedTest("network.scope.bypass.untrusted-wire-context-denied", TARGET)]
    public static void UntrustedWireContextWithCorrectScopeMustBeDenied()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = AuthorizationContext.FromUntrustedWire(userId: SUBJECT);
        auth.AddPermission(Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A context built from untrusted wire data must be denied even when it carries the required scope");
    }

    [GeneratedTest("network.scope.bypass.expired-context-denied", TARGET)]
    public static void ExpiredContextWithCorrectScopeMustBeDenied()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: true);
        auth.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        auth.AddPermission(Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "An expired context must be denied even when verified and carrying the required scope");
    }

    [GeneratedTest("network.scope.bypass.null-context-denied", TARGET)]
    public static void NullContextWithRequiredScopeMustBeDenied()
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(null, required), "A null authorization context must be denied when a scope is required");
    }

    [GeneratedTest("network.scope.bypass.inherited-cannot-add-then-denied", TARGET)]
    public static void InheritedContextCannotGainPermissionAndIsDenied()
    {
        var parent = AuthorizationContext.Create(userId: SUBJECT, isVerified: true);
        var inherited = AuthorizationContext.InheritFrom(parent);

        var added = inherited.AddPermission(Scopes.Boutique.READ);

        IsTrue(!added.IsSuccess, "An inherited context must refuse to gain a permission");

        var required = new HashSet<string>(StringComparer.Ordinal) { Scopes.Boutique.READ };

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(inherited, required), "An inherited context that could not gain the scope must be denied");
    }

    [GeneratedTest("network.scope.bypass.allowself-empty-identity-denied", TARGET)]
    public static void AllowSelfWithEmptyIdentityArgumentMustBeDenied()
    {
        var auth = Principal(string.Empty);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, string.Empty), "An empty identity argument matching an empty subject must not authorize self-access");
    }

    [GeneratedTest("network.scope.bypass.allowself-null-identity-denied", TARGET)]
    public static void AllowSelfWithNullIdentityArgumentMustBeDenied()
    {
        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, null), "A null identity argument must not authorize self-access");
    }

    [GeneratedTest("network.scope.bypass.allowself-null-subject-denied", TARGET)]
    public static void AllowSelfWithNullSubjectMustBeDenied()
    {
        var auth = Principal(null);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, SUBJECT), "A context with a null subject must not authorize self-access");
    }

    [GeneratedTest("network.scope.bypass.allowself-whitespace-mismatch-denied", TARGET)]
    public static void AllowSelfWithWhitespaceMismatchMustBeDenied()
    {
        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, SUBJECT + " "), "A trailing-whitespace identity argument must not match the exact subject");
        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, " " + SUBJECT), "A leading-whitespace identity argument must not match the exact subject");
    }

    [GeneratedTest("network.scope.bypass.allowself-case-mismatch-denied", TARGET)]
    public static void AllowSelfWithCaseMismatchMustBeDenied()
    {
        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, SUBJECT.ToUpperInvariant()), "An upper-cased identity argument must not match the exact-cased subject");
    }

    [GeneratedTest("network.scope.bypass.allowself-different-subject-denied", TARGET)]
    public static void AllowSelfWithDifferentSubjectMustBeDenied()
    {
        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, OTHER), "A different subject must not authorize self-access");
    }

    [GeneratedTest("network.scope.bypass.allowself-unverified-denied", TARGET)]
    public static void AllowSelfWithUnverifiedContextMustBeDenied()
    {
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: false);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, SUBJECT), "An unverified context must not authorize self-access even when the subject matches");
    }

    [GeneratedTest("network.scope.bypass.allowself-expired-denied", TARGET)]
    public static void AllowSelfWithExpiredContextMustBeDenied()
    {
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: true);
        auth.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, SUBJECT), "An expired context must not authorize self-access even when the subject matches");
    }

    [GeneratedTest("network.scope.bypass.allowself-misnamed-param-denied", TARGET)]
    public static void AllowSelfWithMisnamedIdentityParameterMustBeDenied()
    {
        var method = Method<SelfService>(nameof(SelfService.Mismatch));

        IsTrue(ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName), "AllowSelf metadata should resolve");

        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(identityArgument == null, "An AllowSelf identity parameter name that matches no actual parameter must read as null");

        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "A misnamed identity parameter must deny self-access even when the subject would otherwise match");
    }

    [GeneratedTest("network.scope.bypass.allowself-null-argument-value-denied", TARGET)]
    public static void AllowSelfWithNullArgumentValueMustBeDenied()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { null }, parameterName);

        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "A null identity argument value must deny self-access");
    }

}
