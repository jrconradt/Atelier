namespace Atelier.Framework.Network.Attributes
{
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Method,
        AllowMultiple = false,
        Inherited = true)]
    public class RequiresAuthorizationAttribute : Attribute
    {
        public string? Action { get; set; }
        public string? Resource { get; set; }
        public string[]? Roles { get; set; }
        public string[]? Permissions { get; set; }
        public string? Policy { get; set; }

        public RequiresAuthorizationAttribute()
        {
        }

        public RequiresAuthorizationAttribute(
            string action,
            string resource)
        {
            Action = action;
            Resource = resource;
        }
    }
}
