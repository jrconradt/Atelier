using Atelier.Framework.Primitives;
using Atelier.Framework.Context;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using ContextExtensions = Atelier.Framework.Context.Extensions.ContextAuthorizationExtensions;

namespace Atelier.Framework.Network.Interceptors;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class ContextExtractionInterceptor : Interceptor, IAtelier
{

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ApplyContext(context);

        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        finally
        {
            AmbientContext.SetCurrent(null!);
        }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ApplyContext(context);

        try
        {
            return await continuation(requestStream, context).ConfigureAwait(false);
        }
        finally
        {
            AmbientContext.SetCurrent(null!);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ApplyContext(context);

        try
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
        }
        finally
        {
            AmbientContext.SetCurrent(null!);
        }
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ApplyContext(context);

        try
        {
            await continuation(requestStream, responseStream, context).ConfigureAwait(false);
        }
        finally
        {
            AmbientContext.SetCurrent(null!);
        }
    }

    private void ApplyContext(ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.RequestHeaders;
        var contextEntry = metadata.FirstOrDefault(m => m.Key == "x-atelier-context");

        var extractedContext = contextEntry != null && !contextEntry.IsBinary
            ? WireContextCodec.Decode(contextEntry.Value)
            : CreateContextFromMetadata(metadata);

        if (extractedContext != null)
        {
            AmbientContext.SetCurrent(extractedContext);
        }

        if (Logger?.IsEnabled(global::Atelier.Framework.Observability.LogLevel.Debug) ?? false)
        {
            Observe(LogLevel.Debug, values: [("UserIdRedacted", WireContextCodec.RedactIdentifier(extractedContext != null ? ContextExtensions.GetUserId(extractedContext) : null)), ("Method", context.Method)]);
        }
    }

    private static IContext CreateContextFromMetadata(Metadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string? traceId = null;
        string? correlationId = null;
        string? userId = null;

        foreach (var entry in metadata)
        {
            switch (entry.Key)
            {
                case "x-atelier-traceid":
                {
                    traceId = entry.Value;
                    break;
                }
                case "x-atelier-correlationid":
                {
                    correlationId = entry.Value;
                    break;
                }
                case "x-atelier-userid":
                {
                    userId = entry.Value;
                    break;
                }
            }
        }

        return WireContextCodec.CreateUnverifiedFallback(
            "gRPC",
            traceId,
            correlationId,
            userId);
    }
}
