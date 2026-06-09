namespace Atelier.Framework.Offering.Product.Configuration;

public interface IEndpointConfiguration
{
    IEndpointConfiguration MapOperations<TService>(string basePath)
        where TService : class;

    IEndpointConfiguration MapGet(
        string route,
        Delegate handler);

    IEndpointConfiguration MapPost(
        string route,
        Delegate handler);

    IEndpointConfiguration MapPut(
        string route,
        Delegate handler);

    IEndpointConfiguration MapDelete(
        string route,
        Delegate handler);

    IEndpointConfiguration MapPatch(
        string route,
        Delegate handler);

    IEndpointGroup Group(string prefix);
}

public interface IEndpointGroup
{
    IEndpointGroup MapGet(
        string route,
        Delegate handler);

    IEndpointGroup MapPost(
        string route,
        Delegate handler);

    IEndpointGroup MapPut(
        string route,
        Delegate handler);

    IEndpointGroup MapDelete(
        string route,
        Delegate handler);

    IEndpointGroup MapPatch(
        string route,
        Delegate handler);

    IEndpointConfiguration EndGroup();
}
