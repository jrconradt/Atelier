namespace Atelier.Framework.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ApiAttribute : Attribute
    {
        public string[]? Claims { get; set; }

        public ApiAttribute(string[]? claims)
        {
            Claims = claims;
        }
    }
}
