namespace Atelier.Framework.Requisitions;

/// <summary>
/// Declares that <see cref="Service"/> depends on <see cref="Requisite"/> through a
/// <c>[Requisite]</c> member. Emitted by the requisites generator so the edge survives
/// into metadata, where a referencing boutique host can read it.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RequisiteEdgeAttribute : Attribute
{
    public Type Service { get; }

    public Type Requisite { get; }

    public RequisiteEdgeAttribute(Type service, Type requisite)
    {
        Service = service;
        Requisite = requisite;
    }
}
