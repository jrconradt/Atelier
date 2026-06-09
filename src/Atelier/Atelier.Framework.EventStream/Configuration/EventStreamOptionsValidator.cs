using Microsoft.Extensions.Options;

namespace Atelier.Framework.EventStream.Configuration;

public sealed class EventStreamOptionsValidator : IValidateOptions<EventStreamOptions>
{
    public ValidateOptionsResult Validate(string? name, EventStreamOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateDirectory(nameof(options.OffsetStoreDirectory), options.OffsetStoreDirectory, failures);
        ValidateDirectory(nameof(options.HashRegistryDirectory), options.HashRegistryDirectory, failures);

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }

    private static void ValidateDirectory(string propertyName, string value, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"EventStream:{propertyName} must be set to a durable directory path.");
            return;
        }

        if (!Path.IsPathFullyQualified(value))
        {
            failures.Add($"EventStream:{propertyName} must be an absolute path; '{value}' is relative.");
            return;
        }

        if (!IsWritable(value))
        {
            failures.Add($"EventStream:{propertyName} is not writable: '{value}'.");
        }
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe.{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
