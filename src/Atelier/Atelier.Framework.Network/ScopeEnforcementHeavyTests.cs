using System.Reflection;
using Atelier.Framework.Attributes;
using Atelier.Framework.Identity.Authorization;
using Atelier.Framework.Context;
using Atelier.Framework.Network.Enforcement;
using Atelier.Framework.Testing;

namespace Atelier.Framework.Network;

public static class ScopeEnforcementHeavyTests
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

    private sealed class DualScopeService
    {
        [RequiresScope(Scopes.Boutique.READ)]
        [RequiresScope(Scopes.Boutique.WRITE)]
        public void ReadWrite()
        {
        }
    }

    private sealed class NoScopeService
    {
        public void Plain()
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
        public void Named(string userId)
        {
            ArgumentNullException.ThrowIfNull(userId);
        }

        [AllowSelf("userId")]
        public void Mismatched(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
        }

        [AllowSelf("userId")]
        public void TwoArgs(string note,
                            string userId)
        {
            ArgumentNullException.ThrowIfNull(note);
            ArgumentNullException.ThrowIfNull(userId);
        }

        [AllowSelf]
        public void NoIdentityParam()
        {
        }
    }

    private interface IOwnedContract
    {
        void Touch(string ownerId);
    }

    [AllowSelfContract("ownerId")]
    private sealed class OwnedContractService : IOwnedContract
    {
        public void Touch(string ownerId)
        {
            ArgumentNullException.ThrowIfNull(ownerId);
        }
    }

    [ScopeResource(typeof(Scopes.Boutique))]
    private sealed class TieredService
    {
        public void GetBoutique()
        {
        }

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

    private static void IsTrue(bool condition,
                               string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [GeneratedTest("network.scope.heavy.single-present-allow", TARGET)]
    public static void SingleScopePresentAllows()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding the single required scope should be authorized");
    }

    [GeneratedTest("network.scope.heavy.single-missing-deny", TARGET)]
    public static void SingleScopeMissingDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding no scopes should be denied");
    }

    [GeneratedTest("network.scope.heavy.multi-all-present-allow", TARGET)]
    public static void MultipleScopesAllPresentAllows()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<DualScopeService>(nameof(DualScopeService.ReadWrite)));

        IsTrue(required.Count == 2, "Two distinct RequiresScope declarations should yield two requirements");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ, Scopes.Boutique.WRITE);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding both required scopes should be authorized");
    }

    [GeneratedTest("network.scope.heavy.multi-one-missing-deny", TARGET)]
    public static void MultipleScopesOneMissingDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<DualScopeService>(nameof(DualScopeService.ReadWrite)));
        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal missing one of two required scopes should be denied");
    }

    [GeneratedTest("network.scope.heavy.multi-other-missing-deny", TARGET)]
    public static void MultipleScopesOtherMissingDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<DualScopeService>(nameof(DualScopeService.ReadWrite)));
        var auth = Principal(SUBJECT, Scopes.Boutique.WRITE);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal missing the other of two required scopes should be denied");
    }

    [GeneratedTest("network.scope.heavy.empty-requirement-allows", TARGET)]
    public static void EmptyRequirementSetAllows()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NoScopeService>(nameof(NoScopeService.Plain)));

        IsTrue(required.Count == 0, "A method with no scope metadata should resolve to an empty requirement set");

        var auth = Principal(SUBJECT);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "An empty requirement set should authorize any principal");
    }

    [GeneratedTest("network.scope.heavy.empty-requirement-null-auth-allows", TARGET)]
    public static void EmptyRequirementSetWithNullAuthAllows()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<NoScopeService>(nameof(NoScopeService.Plain)));

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(null, required), "An empty requirement set authorizes even a null authorization context by design");
    }

    [GeneratedTest("network.scope.heavy.superset-allows", TARGET)]
    public static void SupersetOfScopesAllows()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, Scopes.Boutique.READ, Scopes.Boutique.WRITE, "atelier.boutique.admin");

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A principal holding a superset of the required scopes should be authorized");
    }

    [GeneratedTest("network.scope.heavy.unverified-with-scope-deny", TARGET)]
    public static void UnverifiedContextWithScopeDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: false);
        auth.AddPermission(Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "An unverified context must be denied even when it carries the required scope");
    }

    [GeneratedTest("network.scope.heavy.from-untrusted-wire-deny", TARGET)]
    public static void FromUntrustedWireWithScopeDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = AuthorizationContext.FromUntrustedWire(userId: SUBJECT);
        auth.AddPermission(Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A context built from untrusted wire must be denied even when it carries the required scope");
    }

    [GeneratedTest("network.scope.heavy.expired-with-scope-deny", TARGET)]
    public static void VerifiedButExpiredContextDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: true);
        auth.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
        auth.AddPermission(Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A verified but expired context must be denied even when it carries the required scope");
    }

    [GeneratedTest("network.scope.heavy.future-expiry-allows", TARGET)]
    public static void VerifiedNotYetExpiredContextAllows()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: true);
        auth.ExpiresAt = DateTime.UtcNow.AddMinutes(5);
        auth.AddPermission(Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A verified context whose expiry is in the future should be authorized");
    }

    [GeneratedTest("network.scope.heavy.null-auth-deny", TARGET)]
    public static void NullAuthorizationWithRequiredScopeDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(null, required), "A null authorization context must be denied when a scope is required");
    }

    [GeneratedTest("network.scope.heavy.null-principal-userid-with-scope-allow", TARGET)]
    public static void NullUserIdPrincipalWithScopeStillScopeAuthorized()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(null, Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Scope authorization does not consult UserId; a verified null-subject principal holding the scope passes the scope gate");
    }

    [GeneratedTest("network.scope.heavy.case-mismatch-deny", TARGET)]
    public static void ScopeCaseMismatchDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, "Atelier.Boutique.Read");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A scope differing only in casing must not satisfy an ordinal scope requirement");
    }

    [GeneratedTest("network.scope.heavy.uppercase-mismatch-deny", TARGET)]
    public static void ScopeUppercaseMismatchDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, "ATELIER.BOUTIQUE.READ");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "An all-uppercase scope must not satisfy a lowercase ordinal scope requirement");
    }

    [GeneratedTest("network.scope.heavy.trailing-whitespace-deny", TARGET)]
    public static void ScopeTrailingWhitespaceDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, Scopes.Boutique.READ + " ");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A scope with trailing whitespace must not satisfy a trimmed ordinal scope requirement");
    }

    [GeneratedTest("network.scope.heavy.leading-whitespace-deny", TARGET)]
    public static void ScopeLeadingWhitespaceDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, " " + Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A scope with leading whitespace must not satisfy a trimmed ordinal scope requirement");
    }

    [GeneratedTest("network.scope.heavy.surrounding-whitespace-deny", TARGET)]
    public static void ScopeSurroundingWhitespaceDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, " atelier.boutique.read ");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A scope padded with surrounding whitespace must not satisfy an exact ordinal scope requirement");
    }

    [GeneratedTest("network.scope.heavy.prefix-substring-deny", TARGET)]
    public static void ScopePrefixSubstringDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, "atelier.boutique.rea");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A scope that is a proper prefix substring must not satisfy the full scope requirement");
    }

    [GeneratedTest("network.scope.heavy.wildcard-does-not-grant-deny", TARGET)]
    public static void WildcardScopeDoesNotGrant()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = Principal(SUBJECT, "atelier.boutique.*", "atelier.*", "*");

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Wildcard-shaped permission strings must not grant a concrete scope under exact-match lookup");
    }

    [GeneratedTest("network.scope.heavy.falsy-permission-value-still-grants", TARGET)]
    public static void FalsyPermissionValueStillGrantsScope()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<SingleScopeService>(nameof(SingleScopeService.Read)));
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: true);
        auth.AddPermission(Scopes.Boutique.READ, false);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A permission stored with a falsy value must NOT grant the scope; presence-only lookup would be a vulnerability");
    }

    [GeneratedTest("network.scope.heavy.tier-mutator-write-present-allow", TARGET)]
    public static void TierMutatorWritePresentAllows()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.UpdateBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.WRITE), "A mutating operation on a bound resource should derive the WRITE tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.WRITE);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding the WRITE tier scope should pass a mutating operation");
    }

    [GeneratedTest("network.scope.heavy.tier-mutator-read-only-deny", TARGET)]
    public static void TierMutatorWithReadOnlyDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.UpdateBoutique)));
        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A mutating operation must reject a principal holding only the READ tier scope");
    }

    [GeneratedTest("network.scope.heavy.tier-reader-read-present-allow", TARGET)]
    public static void TierReaderReadPresentAllows()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.GetBoutique)));

        IsTrue(required.Contains(Scopes.Boutique.READ), "A reader operation on a bound resource should derive the READ tier scope");

        var auth = Principal(SUBJECT, Scopes.Boutique.READ);

        IsTrue(ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "Principal holding the READ tier scope should pass a reader operation");
    }

    [GeneratedTest("network.scope.heavy.allowself-matching-allow", TARGET)]
    public static void AllowSelfMatchingSubjectAllows()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = Principal(SUBJECT);

        IsTrue(ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName), "AllowSelf metadata should resolve on a decorated method");

        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf should permit the matching subject");
    }

    [GeneratedTest("network.scope.heavy.allowself-different-deny", TARGET)]
    public static void AllowSelfDifferentSubjectDenies()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { OTHER }, parameterName);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf must reject a non-matching subject");
    }

    [GeneratedTest("network.scope.heavy.allowself-named-param-matching-allow", TARGET)]
    public static void AllowSelfNamedParameterMatchingAllows()
    {
        var method = Method<SelfService>(nameof(SelfService.Named));
        var auth = Principal(SUBJECT);

        IsTrue(ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName), "AllowSelf with a configured parameter name should resolve");
        IsTrue(string.Equals(parameterName, "userId", StringComparison.Ordinal), "The resolved parameter name should be the configured one");

        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf should match the subject via the configured parameter name");
    }

    [GeneratedTest("network.scope.heavy.allowself-named-param-by-name-not-position", TARGET)]
    public static void AllowSelfReadsArgumentByNameNotPosition()
    {
        var method = Method<SelfService>(nameof(SelfService.TwoArgs));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { "note-value", SUBJECT }, parameterName);

        IsTrue(string.Equals(identityArgument, SUBJECT, StringComparison.Ordinal), "ReadIdentityArgument must select the argument by parameter name, ignoring a leading non-identity argument");
        IsTrue(ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf should match the subject read by name from the second argument");
    }

    [GeneratedTest("network.scope.heavy.allowself-named-wrong-position-not-self", TARGET)]
    public static void AllowSelfDoesNotMatchWrongPositionedValue()
    {
        var method = Method<SelfService>(nameof(SelfService.TwoArgs));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT, OTHER }, parameterName);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "When the subject occupies the non-identity argument slot, AllowSelf must not treat it as self");
    }

    [GeneratedTest("network.scope.heavy.allowself-param-name-mismatch-deny", TARGET)]
    public static void AllowSelfParameterNameMismatchDenies()
    {
        var method = Method<SelfService>(nameof(SelfService.Mismatched));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(identityArgument == null, "A configured identity parameter name that matches no parameter must read null");
        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "A mismatched identity parameter name must deny self even when the subject is supplied");
    }

    [GeneratedTest("network.scope.heavy.allowself-no-identity-param-deny", TARGET)]
    public static void AllowSelfWithNoIdentityParameterDenies()
    {
        var method = Method<SelfService>(nameof(SelfService.NoIdentityParam));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, Array.Empty<object?>(), parameterName);

        IsTrue(identityArgument == null, "A method with no identity parameter must read null");
        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf must deny when no identity argument is present");
    }

    [GeneratedTest("network.scope.heavy.allowself-null-identity-arg-deny", TARGET)]
    public static void AllowSelfNullIdentityArgumentDenies()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { null }, parameterName);

        IsTrue(identityArgument == null, "A null identity argument should read null");
        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf must deny when the identity argument is null");
    }

    [GeneratedTest("network.scope.heavy.allowself-empty-identity-arg-deny", TARGET)]
    public static void AllowSelfEmptyIdentityArgumentDenies()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { string.Empty }, parameterName);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf must deny when the identity argument is empty");
    }

    [GeneratedTest("network.scope.heavy.allowself-null-subject-deny", TARGET)]
    public static void AllowSelfNullSubjectDenies()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = Principal(null);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf must deny when the principal subject is null even if an identity argument is supplied");
    }

    [GeneratedTest("network.scope.heavy.allowself-unverified-deny", TARGET)]
    public static void AllowSelfUnverifiedContextDenies()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: false);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf must deny an unverified context even when the subject matches the identity argument");
    }

    [GeneratedTest("network.scope.heavy.allowself-expired-deny", TARGET)]
    public static void AllowSelfExpiredContextDenies()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var auth = AuthorizationContext.Create(userId: SUBJECT, isVerified: true);
        auth.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "AllowSelf must deny an expired context even when the subject matches the identity argument");
    }

    [GeneratedTest("network.scope.heavy.allowself-null-auth-deny", TARGET)]
    public static void AllowSelfNullAuthorizationDenies()
    {
        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(null, SUBJECT), "AllowSelf must deny a null authorization context");
    }

    [GeneratedTest("network.scope.heavy.allowself-case-sensitive-deny", TARGET)]
    public static void AllowSelfSubjectCaseMismatchDenies()
    {
        var auth = Principal("User-42");

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, SUBJECT), "AllowSelf must use ordinal comparison; a casing-different subject must not match");
    }

    [GeneratedTest("network.scope.heavy.allowself-whitespace-subject-deny", TARGET)]
    public static void AllowSelfSubjectWhitespaceMismatchDenies()
    {
        var auth = Principal(SUBJECT + " ");

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, SUBJECT), "AllowSelf must not match a subject differing by trailing whitespace");
    }

    [GeneratedTest("network.scope.heavy.allowself-contract-matching-allow", TARGET)]
    public static void AllowSelfContractMatchingAllows()
    {
        var method = Method<OwnedContractService>(nameof(OwnedContractService.Touch));
        var auth = Principal(SUBJECT);

        IsTrue(ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName), "A class-level AllowSelfContract should resolve via inheritance");
        IsTrue(string.Equals(parameterName, "ownerId", StringComparison.Ordinal), "The contract identity property name should resolve to the configured value");

        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { SUBJECT }, parameterName);

        IsTrue(ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "A contract-level AllowSelf should permit the matching subject");
    }

    [GeneratedTest("network.scope.heavy.allowself-contract-different-deny", TARGET)]
    public static void AllowSelfContractDifferentSubjectDenies()
    {
        var method = Method<OwnedContractService>(nameof(OwnedContractService.Touch));
        var auth = Principal(SUBJECT);

        ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName);
        var identityArgument = ScopeRequirementResolver.ReadIdentityArgument(method, new object?[] { OTHER }, parameterName);

        IsTrue(!ScopeAuthorizationEvaluator.IsSelf(auth, identityArgument), "A contract-level AllowSelf must reject a non-matching subject");
    }

    [GeneratedTest("network.scope.heavy.no-allowself-does-not-resolve", TARGET)]
    public static void MethodWithoutAllowSelfDoesNotResolve()
    {
        var method = Method<SingleScopeService>(nameof(SingleScopeService.Read));

        IsTrue(!ScopeRequirementResolver.TryResolveAllowSelf(method, out var parameterName), "A method with no AllowSelf metadata must not resolve self-authorization");
        IsTrue(string.Equals(parameterName, string.Empty, StringComparison.Ordinal), "The out parameter name should be empty when AllowSelf does not resolve");
    }

    [GeneratedTest("network.scope.heavy.allowself-alone-no-scope-not-sufficient", TARGET)]
    public static void AllowSelfMethodHasNoRequiredScopes()
    {
        var method = Method<SelfService>(nameof(SelfService.Profile));
        var required = ScopeRequirementResolver.ResolveRequiredScopes(method);

        IsTrue(required.Count == 0, "An AllowSelf method without scope metadata should resolve no required scopes; the self gate is the interceptor's second check");
    }

    [GeneratedTest("network.scope.heavy.tier-mutator-noscope-principal-deny", TARGET)]
    public static void TierMutatorRejectsNoScopePrincipal()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<TieredService>(nameof(TieredService.UpdateBoutique)));
        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A mutating operation must reject a verified principal holding no scopes");
    }

    [GeneratedTest("network.scope.heavy.multi-empty-principal-deny", TARGET)]
    public static void MultipleScopesEmptyPrincipalDenies()
    {
        var required = ScopeRequirementResolver.ResolveRequiredScopes(Method<DualScopeService>(nameof(DualScopeService.ReadWrite)));
        var auth = Principal(SUBJECT);

        IsTrue(!ScopeAuthorizationEvaluator.IsAuthorized(auth, required), "A multi-scope requirement must reject a principal holding no scopes");
    }
}
