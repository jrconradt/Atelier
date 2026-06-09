using System.Diagnostics.CodeAnalysis;

namespace Atelier.Framework.Properties;

public class QueueMessageMetadata : TypedPropertyBag
{
    public const string MESSAGE_ID_KEY = "MessageId";
    public const string CORRELATION_ID_KEY = "CorrelationId";
    public const string TRACE_ID_KEY = "TraceId";
    public const string SOURCE_KEY = "Source";
    public const string DESTINATION_KEY = "Destination";
    public const string MESSAGE_TYPE_KEY = "MessageType";
    public const string PRIORITY_KEY = "Priority";
    public const string RETRY_COUNT_KEY = "RetryCount";
    public const string MAX_RETRIES_KEY = "MaxRetries";
    public const string TIMEOUT_KEY = "Timeout";
    public const string EXPIRATION_KEY = "Expiration";
    public const string CREATED_AT_KEY = "CreatedAt";
    public const string SCHEDULED_AT_KEY = "ScheduledAt";
    public const string PROCESSED_AT_KEY = "ProcessedAt";
    public const string USER_ID_KEY = "UserId";
    public const string SESSION_ID_KEY = "SessionId";
    public const string TENANT_ID_KEY = "TenantId";
    public const string REQUEST_ID_KEY = "RequestId";
    public const string VERSION_KEY = "Version";
    public const string ENVIRONMENT_KEY = "Environment";
    public const string TAGS_KEY = "Tags";
    public const string CATEGORY_KEY = "Category";
    public const string SUBCATEGORY_KEY = "Subcategory";
    public const string SEVERITY_KEY = "Severity";
    public const string STATUS_KEY = "Status";
    public const string ERROR_CODE_KEY = "ErrorCode";
    public const string ERROR_MESSAGE_KEY = "ErrorMessage";

    public string? MessageId
    {
        get => Get<string?>(MESSAGE_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(MESSAGE_ID_KEY, value);
            }
            else
            {
                Remove(MESSAGE_ID_KEY);
            }
        }
    }

    public string? CorrelationId
    {
        get => Get<string?>(CORRELATION_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(CORRELATION_ID_KEY, value);
            }
            else
            {
                Remove(CORRELATION_ID_KEY);
            }
        }
    }

    public string? TraceId
    {
        get => Get<string?>(TRACE_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(TRACE_ID_KEY, value);
            }
            else
            {
                Remove(TRACE_ID_KEY);
            }
        }
    }

    public string? Source
    {
        get => Get<string?>(SOURCE_KEY);
        set
        {
            if (value != null)
            {
                Set(SOURCE_KEY, value);
            }
            else
            {
                Remove(SOURCE_KEY);
            }
        }
    }

    public string? Destination
    {
        get => Get<string?>(DESTINATION_KEY);
        set
        {
            if (value != null)
            {
                Set(DESTINATION_KEY, value);
            }
            else
            {
                Remove(DESTINATION_KEY);
            }
        }
    }

    public string? MessageType
    {
        get => Get<string?>(MESSAGE_TYPE_KEY);
        set
        {
            if (value != null)
            {
                Set(MESSAGE_TYPE_KEY, value);
            }
            else
            {
                Remove(MESSAGE_TYPE_KEY);
            }
        }
    }

    public int? Priority
    {
        get => Get<int?>(PRIORITY_KEY);
        set
        {
            if (value.HasValue)
            {
                Set(PRIORITY_KEY, value.Value);
            }
            else
            {
                Remove(PRIORITY_KEY);
            }
        }
    }

    public int? RetryCount
    {
        get => Get<int?>(RETRY_COUNT_KEY);
        set
        {
            if (value.HasValue)
            {
                Set(RETRY_COUNT_KEY, value.Value);
            }
            else
            {
                Remove(RETRY_COUNT_KEY);
            }
        }
    }

    public int? MaxRetries
    {
        get => Get<int?>(MAX_RETRIES_KEY);
        set
        {
            if (value.HasValue)
            {
                Set(MAX_RETRIES_KEY, value.Value);
            }
            else
            {
                Remove(MAX_RETRIES_KEY);
            }
        }
    }

    public TimeSpan? Timeout
    {
        get => Get<TimeSpan?>(TIMEOUT_KEY);
        set
        {
            if (value.HasValue)
            {
                Set(TIMEOUT_KEY, value.Value);
            }
            else
            {
                Remove(TIMEOUT_KEY);
            }
        }
    }

    public DateTime? Expiration
    {
        get => Get<DateTime?>(EXPIRATION_KEY);
        set
        {
            if (value.HasValue)
            {
                Set(EXPIRATION_KEY, value.Value);
            }
            else
            {
                Remove(EXPIRATION_KEY);
            }
        }
    }

    public DateTime? CreatedAt
    {
        get => Get<DateTime?>(CREATED_AT_KEY);
        set
        {
            if (value.HasValue)
            {
                Set(CREATED_AT_KEY, value.Value);
            }
            else
            {
                Remove(CREATED_AT_KEY);
            }
        }
    }

    public DateTime? ScheduledAt
    {
        get => Get<DateTime?>(SCHEDULED_AT_KEY);
        set
        {
            if (value.HasValue)
            {
                Set(SCHEDULED_AT_KEY, value.Value);
            }
            else
            {
                Remove(SCHEDULED_AT_KEY);
            }
        }
    }

    public DateTime? ProcessedAt
    {
        get => Get<DateTime?>(PROCESSED_AT_KEY);
        set
        {
            if (value.HasValue)
            {
                Set(PROCESSED_AT_KEY, value.Value);
            }
            else
            {
                Remove(PROCESSED_AT_KEY);
            }
        }
    }

    public string? UserId
    {
        get => Get<string?>(USER_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(USER_ID_KEY, value);
            }
            else
            {
                Remove(USER_ID_KEY);
            }
        }
    }

    public string? SessionId
    {
        get => Get<string?>(SESSION_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(SESSION_ID_KEY, value);
            }
            else
            {
                Remove(SESSION_ID_KEY);
            }
        }
    }

    public string? TenantId
    {
        get => Get<string?>(TENANT_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(TENANT_ID_KEY, value);
            }
            else
            {
                Remove(TENANT_ID_KEY);
            }
        }
    }

    public string? RequestId
    {
        get => Get<string?>(REQUEST_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(REQUEST_ID_KEY, value);
            }
            else
            {
                Remove(REQUEST_ID_KEY);
            }
        }
    }

    public string? Version
    {
        get => Get<string?>(VERSION_KEY);
        set
        {
            if (value != null)
            {
                Set(VERSION_KEY, value);
            }
            else
            {
                Remove(VERSION_KEY);
            }
        }
    }

    public string? Environment
    {
        get => Get<string?>(ENVIRONMENT_KEY);
        set
        {
            if (value != null)
            {
                Set(ENVIRONMENT_KEY, value);
            }
            else
            {
                Remove(ENVIRONMENT_KEY);
            }
        }
    }

    public string? Tags
    {
        get => Get<string?>(TAGS_KEY);
        set
        {
            if (value != null)
            {
                Set(TAGS_KEY, value);
            }
            else
            {
                Remove(TAGS_KEY);
            }
        }
    }

    public string? Category
    {
        get => Get<string?>(CATEGORY_KEY);
        set
        {
            if (value != null)
            {
                Set(CATEGORY_KEY, value);
            }
            else
            {
                Remove(CATEGORY_KEY);
            }
        }
    }

    public string? Subcategory
    {
        get => Get<string?>(SUBCATEGORY_KEY);
        set
        {
            if (value != null)
            {
                Set(SUBCATEGORY_KEY, value);
            }
            else
            {
                Remove(SUBCATEGORY_KEY);
            }
        }
    }

    public string? Severity
    {
        get => Get<string?>(SEVERITY_KEY);
        set
        {
            if (value != null)
            {
                Set(SEVERITY_KEY, value);
            }
            else
            {
                Remove(SEVERITY_KEY);
            }
        }
    }

    public string? Status
    {
        get => Get<string?>(STATUS_KEY);
        set
        {
            if (value != null)
            {
                Set(STATUS_KEY, value);
            }
            else
            {
                Remove(STATUS_KEY);
            }
        }
    }

    public string? ErrorCode
    {
        get => Get<string?>(ERROR_CODE_KEY);
        set
        {
            if (value != null)
            {
                Set(ERROR_CODE_KEY, value);
            }
            else
            {
                Remove(ERROR_CODE_KEY);
            }
        }
    }

    public string? ErrorMessage
    {
        get => Get<string?>(ERROR_MESSAGE_KEY);
        set
        {
            if (value != null)
            {
                Set(ERROR_MESSAGE_KEY, value);
            }
            else
            {
                Remove(ERROR_MESSAGE_KEY);
            }
        }
    }
}
