namespace Atelier.Framework.Attributes;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class DomainGatewayAttribute : Attribute
{
    public string SourceDomain { get; }
    public string TargetDomain { get; }
    public Type? StrategyType { get; set; }

    public DomainGatewayAttribute(string sourceDomain, string targetDomain)
    {
        SourceDomain = sourceDomain;
        TargetDomain = targetDomain;
    }
}
