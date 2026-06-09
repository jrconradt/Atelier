
namespace Atelier.Framework.Context
{
    public class AuthorizationContextSnapshot
    {
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? SessionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsInherited { get; set; }
        public Dictionary<string, object> Permissions { get; set; } = new();
        public Dictionary<string, object> Claims { get; set; } = new();
        public Dictionary<string, object> Roles { get; set; } = new();
        public DateTime SnapshotTakenAt { get; set; } = DateTime.UtcNow;
    }
}
