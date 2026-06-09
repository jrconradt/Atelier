using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Atelier.Framework.Attributes;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Queueing.Core;

namespace Atelier.Framework.Queueing.Workers;

[Contract("HandlerDispatch", Version = "1.0", Namespace = "Framework.Queueing.Workers")]
internal sealed class HandlerDispatch
{
    public bool HasHandler { get; init; }
    public string? BuildError { get; init; }
    public Type? InputType { get; init; }
    public Func<QueueMessage, object?>? Deserialize { get; init; }
    public Func<QueueWorkerBase, object, CancellationToken, Task>? Invoke { get; init; }
    public Func<Task, Outcome>? ExtractOutcome { get; init; }
}

internal static class HandlerDispatchCompiler
{
    public static HandlerDispatch Build(Type workerType, string messageType)
    {
        ArgumentNullException.ThrowIfNull(workerType);
        ArgumentNullException.ThrowIfNull(messageType);

        var methods = workerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        MethodInfo? handlerMethod = null;
        foreach (var method in methods)
        {
            var messageHandlerAttr = method.GetCustomAttributes(typeof(Attributes.MessageHandlerAttribute), false)
                .Cast<Attributes.MessageHandlerAttribute>()
                .FirstOrDefault();

            if (messageHandlerAttr != null)
            {
                if (messageHandlerAttr.HandleAllTypes
                    || messageHandlerAttr.MessageType.Equals(messageType, StringComparison.OrdinalIgnoreCase))
                {
                    handlerMethod = method;
                    break;
                }
            }
        }

        if (handlerMethod == null)
        {
            return new HandlerDispatch
            {
                HasHandler = false,
            };
        }

        var parameters = handlerMethod.GetParameters();
        if (parameters.Length < 2)
        {
            return new HandlerDispatch
            {
                HasHandler = true,
                BuildError = $"Handler method '{handlerMethod.Name}' must have at least 2 parameters (input, CancellationToken)",
            };
        }

        var inputType = parameters[0].ParameterType;

        var deserializeMethod = typeof(QueueMessage).GetMethod(
            nameof(QueueMessage.DeserializePayload),
            new[] { typeof(JsonSerializerOptions) });
        if (deserializeMethod == null)
        {
            return new HandlerDispatch
            {
                HasHandler = true,
                BuildError = "Could not find DeserializePayload method",
            };
        }

        return new HandlerDispatch
        {
            HasHandler = true,
            InputType = inputType,
            Deserialize = CompileDeserialize(deserializeMethod.MakeGenericMethod(inputType)),
            Invoke = CompileInvoke(handlerMethod, inputType),
            ExtractOutcome = CompileExtractOutcome(handlerMethod.ReturnType),
        };
    }

    private static Func<QueueMessage, object?> CompileDeserialize(MethodInfo deserializeMethod)
    {
        var messageParam = Expression.Parameter(typeof(QueueMessage), "message");
        var call = Expression.Call(
            messageParam,
            deserializeMethod,
            Expression.Constant(null, typeof(JsonSerializerOptions)));
        var body = Expression.Convert(call, typeof(object));

        return Expression.Lambda<Func<QueueMessage, object?>>(body, messageParam).Compile();
    }

    private static Func<QueueWorkerBase, object, CancellationToken, Task> CompileInvoke(
        MethodInfo handlerMethod,
        Type inputType)
    {
        var targetParam = Expression.Parameter(typeof(QueueWorkerBase), "target");
        var inputParam = Expression.Parameter(typeof(object), "input");
        var tokenParam = Expression.Parameter(typeof(CancellationToken), "token");

        var typedTarget = Expression.Convert(targetParam, handlerMethod.DeclaringType!);
        var typedInput = Expression.Convert(inputParam, inputType);
        var call = Expression.Call(typedTarget, handlerMethod, typedInput, tokenParam);
        var body = Expression.Convert(call, typeof(Task));

        return Expression.Lambda<Func<QueueWorkerBase, object, CancellationToken, Task>>(
            body,
            targetParam,
            inputParam,
            tokenParam).Compile();
    }

    private static Func<Task, Outcome>? CompileExtractOutcome(Type handlerReturnType)
    {
        var resultProperty = handlerReturnType.GetProperty("Result");
        if (resultProperty == null)
        {
            return null;
        }

        var resultType = resultProperty.PropertyType;
        var isSuccess = resultType.GetProperty(nameof(Outcome.IsSuccess));
        if (isSuccess == null)
        {
            return null;
        }

        var taskParam = Expression.Parameter(typeof(Task), "task");
        var typedTask = Expression.Convert(taskParam, handlerReturnType);
        var resultValue = Expression.Property(typedTask, resultProperty);
        var resultVariable = Expression.Variable(resultType, "result");

        var successFactory = typeof(Outcome).GetMethod(nameof(Outcome.Success), Type.EmptyTypes)!;
        var failureFactory = typeof(Outcome).GetMethod(
            nameof(Outcome.Failure),
            Type.EmptyTypes)!;

        var body = Expression.Block(
            new[] { resultVariable },
            Expression.Assign(resultVariable, resultValue),
            Expression.Condition(
                Expression.Property(resultVariable, isSuccess),
                Expression.Call(successFactory),
                Expression.Call(failureFactory)));

        return Expression.Lambda<Func<Task, Outcome>>(body, taskParam).Compile();
    }
}
