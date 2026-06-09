using Atelier.Framework.Observability.Configuration;
using Atelier.Framework.Observability.Formatting;
using Atelier.Framework.Observability.Strategy;
using Atelier.Framework.Requisitions;
using ImplFormatting = Atelier.Framework.Observability.Formatting;
using ImplStrategy = Atelier.Framework.Observability.Strategy;

namespace Atelier.Framework.Observability.Configuration
{
    public sealed partial class LoggingStrategyFactory
    {
        [Requisite] private readonly HttpClient _httpClient = null!;
        [Requisite(Required = false)] private readonly ILogger? _logger = null;

        public ILoggingStrategy CreateFromConfiguration(LoggingConfiguration configuration)
        {
            if (configuration.Outputs.Count == 0)
            {
                return new ImplStrategy.ConsoleLoggingStrategy(new PlainTextFormatter());
            }

            if (configuration.Outputs.Count == 1)
            {
                return CreateOutput(configuration.Outputs[0]);
            }

            var strategies = configuration.Outputs
                .Select(output => CreateOutput(output))
                .ToList();

            return new ImplStrategy.CompositeLoggingStrategy(strategies.ToArray());
        }

        private ILoggingStrategy CreateOutput(LoggingOutputConfiguration output)
        {
            var formatter = CreateFormatter(output.Formatter);
            var strategy = CreateStrategy(
                output.OutputType,
                formatter,
                output.Configuration);

            return strategy;
        }

        private ILogFormatter CreateFormatter(string formatterType)
        {
            return formatterType.ToLowerInvariant() switch
            {
                "plain" => new Formatting.PlainTextFormatter(),
                "json" => new Formatting.JsonFormatter(),
                "compact" => new ImplFormatting.CompactFormatter(),
                _ => throw new InvalidOperationException(
                    $"Unknown log formatter '{formatterType}'. Supported formatters: plain, json, compact.")
            };
        }

        private ILoggingStrategy CreateStrategy(
            string type,
            ILogFormatter formatter,
            Dictionary<string, object> parameters)
        {
            return type.ToLowerInvariant() switch
            {
                "console" => new ImplStrategy.ConsoleLoggingStrategy(formatter),
                "file" => CreateFileStrategy(
                    formatter,
                    parameters),
                "structured" => new ImplStrategy.StructuredLoggingStrategy(formatter),
                "elasticsearch" => CreateElasticsearchStrategy(
                    formatter,
                    parameters),
                _ => throw new InvalidOperationException(
                    $"Unknown log output type '{type}'. Supported output types: console, file, structured, elasticsearch.")
            };
        }

        private ILoggingStrategy CreateFileStrategy(
            ILogFormatter formatter,
            Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue(
                "FilePath",
                out var filePathObj) || filePathObj is not string filePath)
            {
                throw new InvalidOperationException("File strategy requires 'FilePath' parameter");
            }

            return new ImplStrategy.FileLoggingStrategy(
                filePath,
                formatter);
        }

        private ILoggingStrategy CreateElasticsearchStrategy(
            ILogFormatter formatter,
            Dictionary<string, object> parameters)
        {
            var elasticsearchUrl = parameters.TryGetValue("ElasticsearchUrl", out var urlObj) && urlObj is string url
                ? url
                : "https://localhost:9200";
            var indexPattern = parameters.TryGetValue("IndexPattern", out var indexObj) && indexObj is string index
                ? index
                : "atelier-logs";
            var bulkSize = parameters.TryGetValue("BulkSize", out var sizeObj) && sizeObj is int size
                ? size
                : 100;
            var verboseExceptions = parameters.TryGetValue("VerboseExceptions", out var verboseObj) && verboseObj is bool verbose
                ? verbose
                : false;
            var apiKey = parameters.TryGetValue("ApiKey", out var apiKeyObj) && apiKeyObj is string key
                ? key
                : null;
            var basicAuthUser = parameters.TryGetValue("BasicAuthUser", out var userObj) && userObj is string user
                ? user
                : null;
            var basicAuthPassword = parameters.TryGetValue("BasicAuthPassword", out var passwordObj) && passwordObj is string password
                ? password
                : null;
            var allowInsecureTransport = parameters.TryGetValue("AllowInsecureTransport", out var insecureObj) && insecureObj is bool insecure
                ? insecure
                : false;

            return new ImplStrategy.ElasticsearchLoggingStrategy(
                _httpClient,
                formatter,
                _logger)
            .Configure(
                elasticsearchUrl,
                indexPattern,
                bulkSize,
                verboseExceptions,
                apiKey,
                basicAuthUser,
                basicAuthPassword,
                allowInsecureTransport);
        }
    }
}



