using System.Text.Json;

using Atelier.Framework.Context;
using Atelier.Framework.Outcomes;
namespace Atelier.Framework.Context.Validation
{
    public static class ContextSizeValidator
    {
        public const int DEFAULT_MAX_CONTEXT_SIZE_BYTES = 64 * 1024;
        public const int MAX_TRACE_ID_SIZE_BYTES = 256;
        public const int MAX_CORRELATION_ID_SIZE_BYTES = 256;
        public const int MAX_SERVICE_ID_SIZE_BYTES = 128;
        public const int MAX_DOMAIN_ID_SIZE_BYTES = 128;
        public const int MAX_DATA_KEY_SIZE_BYTES = 128;
        public const int MAX_DATA_VALUE_SIZE_BYTES = 1024;
        public const int MAX_INDIVIDUAL_RESULT_SIZE_BYTES = 2048;
        public const int MAX_SERVICE_METADATA_VALUE_SIZE_BYTES = 512;
        public const int MAX_OPTIMIZED_RESULT_COUNT = 10;

        public static ContextSizeValidationResult ValidateFieldSizes(
            IContext context,
            int maxSizeBytes,
            int serializedSizeBytes)
        {
            var validationResult = new ContextSizeValidationResult
            {
                IsValid = true,
                TotalSizeBytes = serializedSizeBytes,
                MaxAllowedSizeBytes = maxSizeBytes
            };

            ValidateFieldSizes(context, validationResult);

            if (serializedSizeBytes > maxSizeBytes)
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add($"Total serialized size ({serializedSizeBytes} bytes) exceeds maximum allowed size ({maxSizeBytes} bytes)");
            }

            return validationResult;
        }

        private static void ValidateFieldSizes(IContext context, ContextSizeValidationResult validationResult)
        {
            if (!string.IsNullOrEmpty(context.TraceId) && System.Text.Encoding.UTF8.GetByteCount(context.TraceId) > MAX_TRACE_ID_SIZE_BYTES)
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add($"TraceId size exceeds maximum ({MAX_TRACE_ID_SIZE_BYTES} bytes)");
            }

            if (!string.IsNullOrEmpty(context.SpanId) && System.Text.Encoding.UTF8.GetByteCount(context.SpanId) > MAX_TRACE_ID_SIZE_BYTES)
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add($"SpanId size exceeds maximum ({MAX_TRACE_ID_SIZE_BYTES} bytes)");
            }

            if (!string.IsNullOrEmpty(context.ParentSpanId) && System.Text.Encoding.UTF8.GetByteCount(context.ParentSpanId) > MAX_TRACE_ID_SIZE_BYTES)
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add($"ParentSpanId size exceeds maximum ({MAX_TRACE_ID_SIZE_BYTES} bytes)");
            }

            if (!string.IsNullOrEmpty(context.CorrelationId) && System.Text.Encoding.UTF8.GetByteCount(context.CorrelationId) > MAX_CORRELATION_ID_SIZE_BYTES)
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add($"CorrelationId size exceeds maximum ({MAX_CORRELATION_ID_SIZE_BYTES} bytes)");
            }

            if (!string.IsNullOrEmpty(context.ServiceId) && System.Text.Encoding.UTF8.GetByteCount(context.ServiceId) > MAX_SERVICE_ID_SIZE_BYTES)
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add($"ServiceId size exceeds maximum ({MAX_SERVICE_ID_SIZE_BYTES} bytes)");
            }

            if (!string.IsNullOrEmpty(context.DomainId) && System.Text.Encoding.UTF8.GetByteCount(context.DomainId) > MAX_DOMAIN_ID_SIZE_BYTES)
            {
                validationResult.IsValid = false;
                validationResult.Violations.Add($"DomainId size exceeds maximum ({MAX_DOMAIN_ID_SIZE_BYTES} bytes)");
            }

            foreach (var kvp in context.Data)
            {
                if (System.Text.Encoding.UTF8.GetByteCount(kvp.Key) > MAX_DATA_KEY_SIZE_BYTES)
                {
                    validationResult.IsValid = false;
                    validationResult.Violations.Add($"Data key '{kvp.Key}' size exceeds maximum ({MAX_DATA_KEY_SIZE_BYTES} bytes)");
                }

                if (System.Text.Encoding.UTF8.GetByteCount(kvp.Value) > MAX_DATA_VALUE_SIZE_BYTES)
                {
                    validationResult.IsValid = false;
                    validationResult.Violations.Add($"Data value for key '{kvp.Key}' size exceeds maximum ({MAX_DATA_VALUE_SIZE_BYTES} bytes)");
                }
            }

            foreach (var kvp in context.Results)
            {
                var resultSize = kvp.Value != null ? EstimateObjectSize(kvp.Value) : 0;
                if (resultSize > MAX_INDIVIDUAL_RESULT_SIZE_BYTES)
                {
                    validationResult.IsValid = false;
                    validationResult.Violations.Add($"Result value for key '{kvp.Key}' size exceeds maximum ({MAX_INDIVIDUAL_RESULT_SIZE_BYTES} bytes)");
                }
            }

            foreach (var kvp in context.ServiceMetadata)
            {
                if (System.Text.Encoding.UTF8.GetByteCount(kvp.Value) > MAX_SERVICE_METADATA_VALUE_SIZE_BYTES)
                {
                    validationResult.IsValid = false;
                    validationResult.Violations.Add($"Service metadata value for key '{kvp.Key}' size exceeds maximum ({MAX_SERVICE_METADATA_VALUE_SIZE_BYTES} bytes)");
                }
            }
        }

        private static int EstimateObjectSize(object obj)
        {
            if (obj == null)
            {
                return 0;
            }

            try
            {
                var json = JsonSerializer.Serialize(obj);
                return System.Text.Encoding.UTF8.GetByteCount(json);
            }
            catch
            {

                return System.Text.Encoding.UTF8.GetByteCount(obj.ToString() ?? string.Empty);
            }
        }

        public static IContext OptimizeForMessaging(
            this IContext context,
            IContextSerializer serializer,
            int maxSizeBytes = DEFAULT_MAX_CONTEXT_SIZE_BYTES)
        {
            var serialized = serializer.Serialize(context);
            var serializedSizeBytes = System.Text.Encoding.UTF8.GetByteCount(serialized);
            var validation = ValidateFieldSizes(context, maxSizeBytes, serializedSizeBytes);
            if (validation.IsValid)
            {
                return context;
            }

            var optimized = context.CreateChild($"{context.Name}-optimized", context.Scope);

            optimized.CorrelationId = context.CorrelationId;
            optimized.TraceId = context.TraceId;
            optimized.SpanId = context.SpanId;
            optimized.ParentSpanId = context.ParentSpanId;
            optimized.ServiceId = context.ServiceId;
            optimized.DomainId = context.DomainId;

            foreach (var kvp in context.GetFilteredData())
            {
                if (System.Text.Encoding.UTF8.GetByteCount(kvp.Key) <= MAX_DATA_KEY_SIZE_BYTES &&
                    System.Text.Encoding.UTF8.GetByteCount(kvp.Value) <= MAX_DATA_VALUE_SIZE_BYTES)
                {
                    optimized.AddValue(kvp.Key, kvp.Value);
                }
            }

            var resultCount = 0;
            foreach (var kvp in context.Results)
            {
                if (resultCount >= MAX_OPTIMIZED_RESULT_COUNT)
                {
                    break;
                }

                var resultSize = EstimateObjectSize(kvp.Value);
                if (resultSize <= MAX_INDIVIDUAL_RESULT_SIZE_BYTES)
                {
                    optimized.AddResult(kvp.Key, kvp.Value);
                    resultCount++;
                }
            }

            if (context.Authorization != null)
            {
                optimized.Authorization = context.Authorization;
            }

            return optimized;
        }
    }
}
