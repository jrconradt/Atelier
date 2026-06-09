using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Atelier.Framework.Observability.Formatting;
using Atelier.Framework.Outcomes;
using Atelier.Framework.Requisitions;

namespace Atelier.Framework.Observability.Strategy
{
    public sealed partial class ElasticsearchLoggingStrategy : ILoggingStrategy, IAsyncDisposable
    {
        private const int CHANNEL_CAPACITY = 16384;

        [Requisite] private readonly HttpClient _httpClient = null!;
        [Requisite(Required = false)] private readonly ILogFormatter _formatter = null!;
        [Requisite(Required = false)] private readonly ILogger? Logger = null;
        private string _elasticsearchUrl = "https://localhost:9200";
        private string _indexPattern = "atelier-logs";
        private int _bulkSize = 100;
        private bool _verboseExceptions = false;
        private TimeSpan _flushInterval = TimeSpan.FromSeconds(2);
        private System.Net.Http.Headers.AuthenticationHeaderValue? _authorization;
        private long _droppedDocuments;
        private long _lastReportedDrops;
        private Channel<BulkDocument> _bulkBuffer = Channel.CreateBounded<BulkDocument>(
            new BoundedChannelOptions(CHANNEL_CAPACITY)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        private readonly CancellationTokenSource _shutdownCts = new();
        private Task? _drainLoop;
        private int _disposed = 0;

        public ElasticsearchLoggingStrategy Configure(
            string elasticsearchUrl = "https://localhost:9200",
            string indexPattern = "atelier-logs",
            int bulkSize = 100,
            bool verboseExceptions = false,
            string? apiKey = null,
            string? basicAuthUser = null,
            string? basicAuthPassword = null,
            bool allowInsecureTransport = false,
            TimeSpan? flushInterval = null)
        {
            var normalizedUrl = elasticsearchUrl.TrimEnd('/');

            var isInsecureEndpoint = normalizedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

            if (isInsecureEndpoint
                && !allowInsecureTransport)
            {
                throw new InvalidOperationException(
                    "Elasticsearch logging requires an https:// endpoint; set allowInsecureTransport only for local development.");
            }

            if (isInsecureEndpoint
                && allowInsecureTransport
                && string.Equals(
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                    "Production",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Elasticsearch logging refuses plaintext http transport in Production; allowInsecureTransport is only honored outside Production.");
            }

            if (isInsecureEndpoint
                && allowInsecureTransport)
            {
                Logger?.WithMessage("Elasticsearch log shipping is using plaintext http transport; identity and PII in log payloads and Basic-auth credentials are exposed in transit")
                    .WithValue("ElasticsearchUrl", normalizedUrl)
                    .WithLevel(LogLevel.Warning)
                    .Log();
            }

            _elasticsearchUrl = normalizedUrl;
            _indexPattern = indexPattern;
            _bulkSize = bulkSize;
            _verboseExceptions = verboseExceptions;
            _flushInterval = flushInterval ?? TimeSpan.FromSeconds(2);
            _authorization = BuildAuthorization(
                apiKey,
                basicAuthUser,
                basicAuthPassword);
            _bulkBuffer = Channel.CreateBounded<BulkDocument>(
                new BoundedChannelOptions(CHANNEL_CAPACITY)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false
                },
                OnDocumentDropped);
            _drainLoop = Task.Run(DrainLoopAsync);
            return this;
        }

        private void OnDocumentDropped(BulkDocument dropped)
        {
            Interlocked.Increment(ref _droppedDocuments);
        }

        private async Task DrainLoopAsync()
        {
            var reader = _bulkBuffer.Reader;
            var batch = new List<BulkDocument>(_bulkSize);

            try
            {
                while (await reader.WaitToReadAsync(_shutdownCts.Token).ConfigureAwait(false))
                {
                    while (batch.Count < _bulkSize
                        && reader.TryRead(out var document))
                    {
                        batch.Add(document);
                    }

                    if (batch.Count > 0)
                    {
                        await BulkIndexDocumentsAsync(batch, _shutdownCts.Token).ConfigureAwait(false);
                        ReportDrops();
                        batch.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ReportDrops()
        {
            var dropped = Interlocked.Read(ref _droppedDocuments);

            if (dropped > _lastReportedDrops)
            {
                Logger?.WithMessage("Elasticsearch log buffer overflow dropped documents")
                    .WithValue("DroppedTotal", dropped)
                    .WithLevel(LogLevel.Warning)
                    .Log();
                _lastReportedDrops = dropped;
            }
        }

        private static System.Net.Http.Headers.AuthenticationHeaderValue? BuildAuthorization(
            string? apiKey,
            string? basicAuthUser,
            string? basicAuthPassword)
        {
            if (!string.IsNullOrEmpty(apiKey))
            {
                return new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", apiKey);
            }

            if (!string.IsNullOrEmpty(basicAuthUser)
                && !string.IsNullOrEmpty(basicAuthPassword))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{basicAuthUser}:{basicAuthPassword}"));
                return new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            return null;
        }

        public async Task TraverseAsync(
            LoggingContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var logDocument = CreateLogDocument(context);
                await AddToBulkBufferAsync(logDocument, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.WithMessage("ElasticsearchLoggingStrategy error")
                    .WithError(ex)
                    .WithLevel(LogLevel.Error)
                    .Log();
            }
        }

        private sealed record BulkDocument(string IndexName, object Source);

        private BulkDocument CreateLogDocument(LoggingContext context)
        {
            var timestamp = DateTime.UtcNow;
            var indexName = $"{_indexPattern}-{timestamp:yyyy.MM.dd}";

            var source = new
            {
                timestamp = timestamp,
                level = context.Level.ToString().ToLowerInvariant(),
                message = context.Message,
                serviceName = GetServiceName(context),
                source = GetSource(context),
                workspace = "atelier-refresh",
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "development",
                correlationId = GetCorrelationId(context),
                traceId = GetTraceId(context),
                spanId = GetSpanId(context),
                exception = BuildExceptionField(context.Exception),
                properties = context.Values,
                tags = GetTags(context),
                formatted_message = _formatter.Format(context)
            };

            return new BulkDocument(indexName, source);
        }

        private string GetServiceName(LoggingContext context)
        {
            if (context.Values.TryGetValue("ServiceName", out var serviceName))
            {
                return serviceName.ToString() ?? "unknown";
            }

            if (context.Values.TryGetValue("ApplicationName", out var appName))
            {
                return appName.ToString() ?? "unknown";
            }

            return Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "atelier-service";
        }

        private string GetSource(LoggingContext context)
        {
            if (context.Values.TryGetValue("Source", out var source))
            {
                return source.ToString() ?? "unknown";
            }

            return context.Context?.GetType().Name ?? "unknown";
        }

        private string? GetCorrelationId(LoggingContext context)
        {
            if (context.Values.TryGetValue("CorrelationId", out var correlationId))
            {
                return correlationId.ToString();
            }

            return null;
        }

        private string? GetTraceId(LoggingContext context)
        {
            if (context.Values.TryGetValue("TraceId", out var traceId))
            {
                return traceId.ToString();
            }

            return null;
        }

        private string? GetSpanId(LoggingContext context)
        {
            if (context.Values.TryGetValue("SpanId", out var spanId))
            {
                return spanId.ToString();
            }

            return null;
        }

        private List<string> GetTags(LoggingContext context)
        {
            var tags = new List<string>();

            if (context.Values.TryGetValue("Tags", out var tagsValue) && tagsValue is List<string> tagList)
            {
                tags.AddRange(tagList);
            }

            tags.Add($"level:{context.Level.ToString().ToLowerInvariant()}");
            tags.Add($"environment:{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "development"}");

            return tags;
        }

        private const int MaxExceptionMessageLength = 256;

        private object? BuildExceptionField(Exception? exception)
        {
            if (exception is null)
            {
                return null;
            }

            return new
            {
                type = exception.GetType().FullName,
                message = RedactExceptionMessage(exception.Message),
                stackTrace = _verboseExceptions ? SensitiveValueRedactor.RedactText(exception.StackTrace) : null
            };
        }

        private static string RedactExceptionMessage(string? message)
        {
            var scrubbed = SensitiveValueRedactor.RedactText(message);

            if (scrubbed.Length > MaxExceptionMessageLength)
            {
                return $"{scrubbed.Substring(0, MaxExceptionMessageLength)}…";
            }

            return scrubbed;
        }

        private Task AddToBulkBufferAsync(BulkDocument logDocument, CancellationToken cancellationToken)
        {
            _bulkBuffer.Writer.TryWrite(logDocument);
            return Task.CompletedTask;
        }

        private static int CountBulkItemFailures(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
            {
                return 0;
            }

            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (!root.TryGetProperty("errors", out var errorsElement)
                || errorsElement.ValueKind != JsonValueKind.True)
            {
                return 0;
            }

            if (!root.TryGetProperty("items", out var itemsElement)
                || itemsElement.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var failures = 0;

            foreach (var item in itemsElement.EnumerateArray())
            {
                foreach (var action in item.EnumerateObject())
                {
                    if (action.Value.TryGetProperty("error", out _))
                    {
                        failures++;
                    }
                }
            }

            return failures;
        }

        private static readonly JsonSerializerOptions BulkSerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private async Task BulkIndexDocumentsAsync(List<BulkDocument> documents, CancellationToken ct = default)
        {
            try
            {
                var newline = Encoding.UTF8.GetBytes("\n");
                var bufferWriter = new ArrayBufferWriter<byte>();

                foreach (var doc in documents)
                {
                    using (var actionWriter = new Utf8JsonWriter(bufferWriter))
                    {
                        actionWriter.WriteStartObject();
                        actionWriter.WriteStartObject("index");
                        actionWriter.WriteString("_index", doc.IndexName);
                        actionWriter.WriteEndObject();
                        actionWriter.WriteEndObject();
                        await actionWriter.FlushAsync(ct).ConfigureAwait(false);
                    }
                    bufferWriter.Write(newline);

                    using (var sourceWriter = new Utf8JsonWriter(bufferWriter))
                    {
                        JsonSerializer.Serialize(sourceWriter, doc.Source, BulkSerializerOptions);
                        await sourceWriter.FlushAsync(ct).ConfigureAwait(false);
                    }
                    bufferWriter.Write(newline);
                }

                var content = new ByteArrayContent(bufferWriter.WrittenSpan.ToArray());
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-ndjson");

                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{_elasticsearchUrl}/_bulk")
                {
                    Content = content
                };

                if (_authorization is not null)
                {
                    request.Headers.Authorization = _authorization;
                }

                var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    Logger?.WithMessage("Elasticsearch bulk indexing failed")
                        .WithValue("StatusCode", response.StatusCode)
                        .WithValue("ErrorContent", errorContent)
                        .WithValue("DocumentCount", documents.Count)
                        .WithLevel(LogLevel.Error)
                        .Log();
                    return;
                }

                var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var failedItems = CountBulkItemFailures(responseBody);

                if (failedItems > 0)
                {
                    Logger?.WithMessage("Elasticsearch bulk indexing rejected individual documents")
                        .WithValue("FailedItemCount", failedItems)
                        .WithValue("DocumentCount", documents.Count)
                        .WithLevel(LogLevel.Error)
                        .Log();
                }
            }
            catch (Exception ex)
            {
                Logger?.WithMessage("Elasticsearch bulk indexing error")
                    .WithError(ex)
                    .WithValue("DocumentCount", documents.Count)
                    .WithLevel(LogLevel.Error)
                    .Log();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _bulkBuffer.Writer.TryComplete();

            if (_drainLoop is not null)
            {
                try
                {
                    await _drainLoop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _shutdownCts.Cancel();
            _shutdownCts.Dispose();
            ReportDrops();
        }
    }
}
