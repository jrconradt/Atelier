using Atelier.Framework.Outcomes;

namespace Atelier.Framework.Context
{
    public class AuthorizationContext
    {
        private readonly Dictionary<string, object> _permissions = new();
        private readonly Dictionary<string, object> _claims = new();
        private readonly Dictionary<string, object> _roles = new();

        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? SessionId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public bool IsInherited { get; set; }
        public bool IsVerified { get; set; }
        public IReadOnlyDictionary<string, object> Permissions => _permissions;
        public IReadOnlyDictionary<string, object> Claims => _claims;
        public IReadOnlyDictionary<string, object> Roles => _roles;
        public static AuthorizationContext Create(
            string? userId = null,
            string? tenantId = null,
            string? sessionId = null,
            bool isVerified = true)
        {
            return new AuthorizationContext
            {
                UserId = userId,
                TenantId = tenantId,
                SessionId = sessionId,
                IsInherited = false,
                IsVerified = isVerified
            };
        }

        public static AuthorizationContext FromUntrustedWire(
            string? userId = null,
            string? tenantId = null,
            string? sessionId = null)
        {
            return new AuthorizationContext
            {
                UserId = userId,
                TenantId = tenantId,
                SessionId = sessionId,
                IsInherited = false,
                IsVerified = false
            };
        }

        public static AuthorizationContext InheritFrom(AuthorizationContext parent)
        {
            return new AuthorizationContext
            {
                UserId = parent.UserId,
                TenantId = parent.TenantId,
                SessionId = parent.SessionId,
                CreatedAt = parent.CreatedAt,
                ExpiresAt = parent.ExpiresAt,
                IsInherited = true,
                IsVerified = parent.IsVerified
            };
        }
        public Outcome<AuthorizationContext> AddPermission(string permission, object? value = null)
        {
            if (IsInherited)
            {
                return Outcome<AuthorizationContext>.Failure();
            }
            _permissions[permission] = value ?? true;
            return Outcome<AuthorizationContext>.Success(this);
        }
        public Outcome<AuthorizationContext> AddClaim(string claim, object value)
        {
            if (IsInherited)
            {
                return Outcome<AuthorizationContext>.Failure();
            }
            _claims[claim] = value;
            return Outcome<AuthorizationContext>.Success(this);
        }
        public Outcome<AuthorizationContext> AddRole(string role, object? value = null)
        {
            if (IsInherited)
            {
                return Outcome<AuthorizationContext>.Failure();
            }
            _roles[role] = value ?? true;
            return Outcome<AuthorizationContext>.Success(this);
        }
        public bool HasPermission(string permission)
        {
            return _permissions.TryGetValue(permission, out var value)
                && value is true;
        }
        public T? GetPermission<T>(string permission)
        {
            if (_permissions.TryGetValue(permission, out var value) && value is T typed)
            {
                return typed;
            }
            return default;
        }
        public bool HasClaim(string claim)
        {
            return _claims.ContainsKey(claim);
        }
        public T? GetClaim<T>(string claim)
        {
            if (_claims.TryGetValue(claim, out var value) && value is T typed)
            {
                return typed;
            }
            return default;
        }
        public bool HasRole(string role)
        {
            return _roles.ContainsKey(role);
        }
        public T? GetRole<T>(string role)
        {
            if (_roles.TryGetValue(role, out var value) && value is T typed)
            {
                return typed;
            }
            return default;
        }
        public bool IsValid()
        {
            if (!IsVerified)
            {
                return false;
            }
            if (ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value)
            {
                return false;
            }
            return true;
        }

        public AuthorizationContextSnapshot CreateSnapshot()
        {
            return new AuthorizationContextSnapshot
            {
                UserId = UserId,
                TenantId = TenantId,
                SessionId = SessionId,
                CreatedAt = CreatedAt,
                ExpiresAt = ExpiresAt,
                IsInherited = IsInherited,
                Permissions = Permissions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Claims = Claims.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Roles = Roles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                SnapshotTakenAt = DateTime.UtcNow
            };
        }
    }
}
