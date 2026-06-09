namespace Atelier.Framework.Context
{
    public class AuthorizationSummary
    {
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public string? SessionId { get; set; }
        public bool IsInherited { get; set; }
        public bool IsValid { get; set; }
        public int PermissionsCount { get; set; }
        public int RolesCount { get; set; }
    }
}
