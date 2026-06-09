namespace Atelier.Framework.Observability;

public interface IAtelier
{
    public void Observe(
        LogLevel level = LogLevel.Information,
        Exception? exception = null,
        string? message = null,
        params ReadOnlySpan<(string Key, object Value)> values);
}
