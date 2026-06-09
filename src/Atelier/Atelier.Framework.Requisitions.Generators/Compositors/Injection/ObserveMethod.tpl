public virtual void Observe(
    global::Atelier.Framework.Observability.LogLevel level = global::Atelier.Framework.Observability.LogLevel.Information,
    global::System.Exception? exception = null,
    string? message = null,
    params global::System.ReadOnlySpan<(string Key, object Value)> values)
{
    if (Logger is null || !Logger.IsEnabled(level))
    {
        return;
    }
    var builder = Logger.WithLevel(level);
    var __contextId = ContextAccessor?.Current?.ContextId;
    if (__contextId is not null)
    {
        builder = builder.WithValue("ContextId", __contextId);
    }
    if (message is not null)
    {
        builder = builder.WithMessage(message);
    }
    if (exception is not null)
    {
        builder = builder.WithError(exception);
    }
    if (!values.IsEmpty)
    {
        builder = builder.WithValues(values);
    }
    builder.Log();
}
