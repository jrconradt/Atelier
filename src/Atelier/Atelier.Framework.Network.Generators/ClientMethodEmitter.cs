using Microsoft.CodeAnalysis;
using Templar.Rendering;
using Atelier.Framework.Network.Generators.Compositors;
using G = Atelier.Framework.Network.Generators.Templates.Transport;

namespace Atelier.Framework.Network.Transport.CodeGen;

internal static class ClientMethodEmitter
{
    public static Compositor Emit(IMethodSymbol method) => new G.ClientMethod
    {
        ReturnType = method.ReturnType.ToDisplayString(),
        MethodName = method.Name,
        ParameterList = Sequence.CommaList(method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .Select(p => (Compositor)new ParameterDecl
            {
                Type = p.Type.ToDisplayString(),
                Name = p.Name,
            })
            .Append(new ParameterDecl
            {
                Type = "System.Threading.CancellationToken",
                Name = "cancellationToken = default",
            })),
        PayloadInit = Payload(method),
        DeserializeBlock = Deserialize(method),
    };

    private static PayloadStrategy Payload(IMethodSymbol method)
    {
        var nonCt = method.Parameters
            .Where(p => p.Type.ToDisplayString() != "System.Threading.CancellationToken")
            .ToList();

        if (nonCt.Count == 0)
        {
            return new EmptyPayload();
        }

        return new SingleParamPayload { ParamName = nonCt[0].Name };
    }

    private static DeserializeStrategy Deserialize(IMethodSymbol method)
    {
        if (method.ReturnType is not INamedTypeSymbol named
            || (named.ConstructedFrom.Name != "Task" && named.ConstructedFrom.Name != "ValueTask")
            || named.TypeArguments.Length == 0)
        {
            return new NoneDeserialize();
        }

        var argSymbol = named.TypeArguments[0];
        var argType = argSymbol.ToDisplayString();

        if (argSymbol is INamedTypeSymbol arg
            && arg.Name == "Outcome")
        {
            return new OutcomeDeserialize { ArgType = argType };
        }

        return new PlainDeserialize { ArgType = argType };
    }
}
