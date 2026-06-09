using Atelier.Framework.Primitives;
using Atelier.Framework.Context;
using Atelier.Framework.Context.Extensions;
using Atelier.Framework.Infrastructure;
using Atelier.Framework.Attributes;
using Atelier.Framework.Observability;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Messaging;

[Infrastructure(InfrastructureLifetime.Scoped)]
[NetworkZone(typeof(Atelier.Framework.Primitives.Application))]
public partial class HandlerRegistry : IAtelier, IHandlerRegistry
{
    private static readonly TimeSpan DefaultDispatchDeadline = TimeSpan.FromSeconds(30);
    public static bool DispatchDeadlineEnabled = true;

    [Requisite] protected readonly IHandlerFactory _handlerFactory = null!;
    [Requisite] protected readonly IContextAccessor _contextAccessor = null!;

    public async Task<Outcome<TResponse>> HandleAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var requestTypeName = typeof(TRequest).Name;
        var responseTypeName = typeof(TResponse).Name;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var dispatchContext = _contextAccessor.Current;
        var callerTraceId = dispatchContext.TraceId;
        var callerSpanId = dispatchContext.SpanId;
        var callerParentSpanId = dispatchContext.ParentSpanId;
        var callerCorrelationId = dispatchContext.CorrelationId;

        dispatchContext.InitializeTracing();

        ApplicationMetrics.MessagingDispatchTotal.WithLabels(
            requestTypeName,
            "attempt",
            ApplicationMetrics.InstanceId,
            ApplicationMetrics.BoutiqueMode).Inc();

        if (Logger?.IsEnabled(LogLevel.Information) == true)
        {
            Observe(LogLevel.Information, values: [("RequestType", requestTypeName), ("ResponseType", responseTypeName)],
            message: $"Dispatching {requestTypeName} to handler returning {responseTypeName}");
        }

        var handler = _handlerFactory.GetHandler<TRequest, TResponse>();

        if (handler == null)
        {
            ApplicationMetrics.MessagingDispatchErrorsTotal.WithLabels(
                requestTypeName,
                "handler_not_found",
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Inc();

            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "No handler registered"), ("RequestType", requestTypeName), ("ResponseType", responseTypeName)]);

            dispatchContext.TraceId = callerTraceId;
            dispatchContext.SpanId = callerSpanId;
            dispatchContext.ParentSpanId = callerParentSpanId;
            dispatchContext.CorrelationId = callerCorrelationId;

            return Outcome<TResponse>.Failure();
        }

        CancellationTokenSource? deadlineSource = null;
        var dispatchToken = cancellationToken;

        if (DispatchDeadlineEnabled)
        {
            deadlineSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadlineSource.CancelAfter(DefaultDispatchDeadline);
            dispatchToken = deadlineSource.Token;
        }

        try
        {
            var result = await handler.HandleAsync(request, dispatchToken).ConfigureAwait(false);

            stopwatch.Stop();
            var resultLabel = result.IsSuccess ? "success" : "failure";

            ApplicationMetrics.MessagingDispatchDuration.WithLabels(
                requestTypeName,
                resultLabel,
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Observe(stopwatch.Elapsed.TotalSeconds);

            if (result.IsSuccess)
            {
                if (Logger?.IsEnabled(LogLevel.Information) == true)
                {
                    Observe(LogLevel.Information, values: [("RequestType", requestTypeName)],
                    message: $"Handled {requestTypeName} successfully");
                }
            }
            else
            {
                ApplicationMetrics.MessagingDispatchErrorsTotal.WithLabels(
                    requestTypeName,
                    "handler_failure",
                    ApplicationMetrics.InstanceId,
                    ApplicationMetrics.BoutiqueMode).Inc();

                Observe(
                    LogLevel.Warning,
                    null,
                    values: [("Reason", "Handler returned failure"), ("RequestType", requestTypeName)]);
            }

            return result;
        }
        catch (OperationCanceledException) when (deadlineSource?.IsCancellationRequested == true
            && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            ApplicationMetrics.MessagingDispatchDuration.WithLabels(
                requestTypeName,
                "timeout",
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Observe(stopwatch.Elapsed.TotalSeconds);

            ApplicationMetrics.MessagingDispatchErrorsTotal.WithLabels(
                requestTypeName,
                "operation_timeout",
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Inc();

            Observe(
                LogLevel.Error,
                null,
                values: [("Reason", "Handler exceeded the dispatch deadline"), ("RequestType", requestTypeName), ("DeadlineSeconds", DefaultDispatchDeadline.TotalSeconds)]);

            return Outcome<TResponse>.Failure();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            ApplicationMetrics.MessagingDispatchDuration.WithLabels(
                requestTypeName,
                "cancelled",
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Observe(stopwatch.Elapsed.TotalSeconds);

            ApplicationMetrics.MessagingDispatchErrorsTotal.WithLabels(
                requestTypeName,
                "operation_cancelled",
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Inc();

            Observe(
                LogLevel.Information,
                null,
                values: [("Reason", "Dispatch cancelled by caller"), ("RequestType", requestTypeName)]);

            return Outcome<TResponse>.Failure();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            ApplicationMetrics.MessagingDispatchDuration.WithLabels(
                requestTypeName,
                "exception",
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Observe(stopwatch.Elapsed.TotalSeconds);

            ApplicationMetrics.MessagingDispatchErrorsTotal.WithLabels(
                requestTypeName,
                "handler_exception",
                ApplicationMetrics.InstanceId,
                ApplicationMetrics.BoutiqueMode).Inc();

            Observe(
                LogLevel.Error,
                ex,
                values: [("Reason", "Handler threw an exception"), ("RequestType", requestTypeName), ("ExceptionType", ex.GetType().Name)]);

            return Outcome<TResponse>.Failure();
        }
        finally
        {
            deadlineSource?.Dispose();

            dispatchContext.TraceId = callerTraceId;
            dispatchContext.SpanId = callerSpanId;
            dispatchContext.ParentSpanId = callerParentSpanId;
            dispatchContext.CorrelationId = callerCorrelationId;
        }
    }
}

