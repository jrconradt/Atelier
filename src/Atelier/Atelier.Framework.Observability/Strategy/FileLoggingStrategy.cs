using System.Text;
using System.Threading.Channels;
using Atelier.Framework.Observability.Formatting;

namespace Atelier.Framework.Observability.Strategy
{
    public sealed class FileLoggingStrategy : ILoggingStrategy, IAsyncDisposable
    {
        private const int CHANNEL_CAPACITY = 8192;
        private const long DEFAULT_MAX_FILE_BYTES = 50L * 1024 * 1024;
        private const int DEFAULT_RETAINED_FILE_COUNT = 5;

        private readonly ILogFormatter _formatter;
        private readonly string _filePath;
        private readonly long _maxFileBytes;
        private readonly int _retainedFileCount;
        private readonly Channel<string> _channel;
        private readonly Task _drainTask;
        private readonly Encoding _encoding = new UTF8Encoding(false);

        private StreamWriter _writer;
        private long _currentSize;

        public FileLoggingStrategy(
            string filePath,
            ILogFormatter? formatter = null,
            long maxFileBytes = DEFAULT_MAX_FILE_BYTES,
            int retainedFileCount = DEFAULT_RETAINED_FILE_COUNT)
        {
            ArgumentNullException.ThrowIfNull(filePath);
            _formatter = formatter ?? new CompactFormatter();
            _filePath = filePath;
            _maxFileBytes = maxFileBytes;
            _retainedFileCount = retainedFileCount;

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _writer = OpenWriter(out _currentSize);

            _channel = Channel.CreateBounded<string>(
                new BoundedChannelOptions(CHANNEL_CAPACITY)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false
                });

            _drainTask = Task.Run(DrainAsync);
        }

        public async Task TraverseAsync(
            LoggingContext loggingContext,
            CancellationToken cancellationToken = default)
        {
            var formatted = _formatter.Format(loggingContext);
            await _channel.Writer.WriteAsync(formatted, cancellationToken).ConfigureAwait(false);
        }

        private async Task DrainAsync()
        {
            var reader = _channel.Reader;

            try
            {
                while (await reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (reader.TryRead(out var line))
                    {
                        await WriteLineAsync(line).ConfigureAwait(false);
                    }

                    await _writer.FlushAsync().ConfigureAwait(false);
                }

                await _writer.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _channel.Writer.TryComplete(ex);
                await Console.Error.WriteLineAsync($"FileLoggingStrategy drain loop terminated for '{_filePath}': {ex}").ConfigureAwait(false);
            }
        }

        private async Task WriteLineAsync(string line)
        {
            var projected = _currentSize + _encoding.GetByteCount(line) + _encoding.GetByteCount(Environment.NewLine);

            if (projected > _maxFileBytes
                && _currentSize > 0)
            {
                await RotateAsync().ConfigureAwait(false);
            }

            await _writer.WriteLineAsync(line).ConfigureAwait(false);
            _currentSize += _encoding.GetByteCount(line) + _encoding.GetByteCount(Environment.NewLine);
        }

        private async Task RotateAsync()
        {
            await _writer.FlushAsync().ConfigureAwait(false);
            await _writer.DisposeAsync().ConfigureAwait(false);

            var index = _retainedFileCount;
            while (index >= 1)
            {
                var source = index == 1
                    ? _filePath
                    : $"{_filePath}.{index - 1}";
                var destination = $"{_filePath}.{index}";

                if (File.Exists(source))
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }

                    File.Move(source, destination);
                }

                index--;
            }

            _writer = OpenWriter(out _currentSize);
        }

        private StreamWriter OpenWriter(out long size)
        {
            var stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);

            size = stream.Length;

            return new StreamWriter(stream, _encoding)
            {
                AutoFlush = false
            };
        }

        public async ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            await _drainTask.ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
            await _writer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
