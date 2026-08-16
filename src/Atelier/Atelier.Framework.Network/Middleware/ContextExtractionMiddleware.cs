using Atelier.Framework.Primitives;
using System.Runtime.CompilerServices;
using Atelier.Framework.Context;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Requisitions;
using Microsoft.AspNetCore.Http;
using Atelier.Framework.Infrastructure.Operation;
using ContextExtensions = Atelier.Framework.Context.Extensions.ContextAuthorizationExtensions;

namespace Atelier.Framework.Network.Middleware;

[Infrastructure(InfrastructureLifetime.Singleton)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class ContextExtractionMiddleware : IAtelier
{
    [Requisite] private readonly IContextAccessor _contextAccessor = null!;

    private readonly StrongBox<RequestDelegate?> _next = new(null);
    private readonly StrongBox<IIdentityVerifier?> _verifier = new(null);

    public ContextExtractionMiddleware Configure(RequestDelegate next, IIdentityVerifier? verifier = null)
    {
        ArgumentNullException.ThrowIfNull(next);

        _next.Value = next;
        _verifier.Value = verifier;
        return this;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (_next.Value is null)
        {
            Observe(LogLevel.Error, values: [("Error", $"{nameof(ContextExtractionMiddleware)} was not configured with a RequestDelegate. Call Configure(next) before InvokeAsync.")]);
            return;
        }

        IContext? extractedContext = null;

        if (httpContext.Request.Headers.TryGetValue(WireContextCodec.CANONICAL_HEADER_NAME, out var contextHeader))
        {
            extractedContext = WireContextCodec.Decode(contextHeader.ToString());
        }
        else if (httpContext.Request.Headers.TryGetValue("X-Atelier-UserId", out _))
        {
            extractedContext = CreateContextFromHeaders(httpContext.Request.Headers);
        }

        if (httpContext.Request.Headers.TryGetValue(Transport.ContextHeaderInjector.TRACEPARENT_HEADER_NAME, out var traceParentHeader)
            && TracingContext.TryParseTraceParent(traceParentHeader.ToString(), out var wireTraceId, out var wireParentSpanId))
        {
            extractedContext ??= new CompositeContext(
                Guid.NewGuid().ToString(),
                "HTTP",
                null,
                new Dictionary<string, string>());

            if (string.IsNullOrEmpty(extractedContext.TraceId))
            {
                extractedContext.TraceId = wireTraceId;
            }

            if (string.IsNullOrEmpty(extractedContext.ParentSpanId))
            {
                extractedContext.ParentSpanId = wireParentSpanId;
            }
        }

        if (extractedContext != null)
        {
            extractedContext.AdoptParentSpan(
                extractedContext.TraceId,
                extractedContext.SpanId ?? extractedContext.ParentSpanId,
                extractedContext.CorrelationId);
        }

        var verifiedAuthorization = await VerifyBearerTokenAsync(httpContext).ConfigureAwait(false);
        if (verifiedAuthorization != null)
        {
            var verifiedContext = new CompositeContext(
                Guid.NewGuid().ToString(),
                "HTTP",
                null,
                new Dictionary<string, string>());

            verifiedContext.TraceId = extractedContext?.TraceId;
            verifiedContext.SpanId = extractedContext?.SpanId;
            verifiedContext.ParentSpanId = extractedContext?.ParentSpanId;
            verifiedContext.CorrelationId = extractedContext?.CorrelationId;
            verifiedContext.Authorization = verifiedAuthorization;
            if (string.IsNullOrEmpty(verifiedContext.SpanId))
            {
                verifiedContext.AdoptParentSpan(
                    verifiedContext.TraceId,
                    verifiedContext.ParentSpanId,
                    verifiedContext.CorrelationId);
            }
            extractedContext = verifiedContext;
        }

        if (extractedContext != null)
        {
            if (httpContext.Request.Headers.TryGetValue("Authorization", out var authVal))
            {
                extractedContext.AddValue("Authorization", authVal.ToString());
            }

            Observe(LogLevel.Debug, values: [("Operation", nameof(InvokeAsync)), ("UserIdRedacted", WireContextCodec.RedactIdentifier(ContextExtensions.GetUserId(extractedContext)))]);

            _contextAccessor.SetCurrent(extractedContext);
        }
        else
        {
            Observe(LogLevel.Debug, values: [("Operation", nameof(InvokeAsync)), ("ContextExtracted", false)]);
        }

        try
        {
            await _next.Value(httpContext).ConfigureAwait(false);
        }
        finally
        {
            _contextAccessor.SetCurrent(null!);
        }
    }

    private async Task<AuthorizationContext?> VerifyBearerTokenAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (_verifier.Value == null
            || !httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return null;
        }

        var raw = authHeader.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = raw.Substring("Bearer ".Length).Trim();
        var verified = await _verifier.Value.VerifyAsync(token, httpContext.RequestAborted).ConfigureAwait(false);
        if (!verified.IsSuccess
            || verified.Data == null)
        {
            Observe(LogLevel.Warning, values: [("Operation", "TokenVerification")]);
            return null;
        }

        return verified.Data;
    }

    private static IContext CreateContextFromHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var traceId = headers.TryGetValue("X-Atelier-TraceId", out var traceIdValue)
            ? traceIdValue.ToString()
            : null;

        var correlationId = headers.TryGetValue("X-Atelier-CorrelationId", out var correlationIdValue)
            ? correlationIdValue.ToString()
            : null;

        var userId = headers.TryGetValue("X-Atelier-UserId", out var userIdValue)
            ? userIdValue.ToString()
            : null;

        return WireContextCodec.CreateUnverifiedFallback(
            "HTTP",
            traceId,
            correlationId,
            userId);
    }
}
