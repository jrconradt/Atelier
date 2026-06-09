namespace Atelier.Framework.Properties;

public class StateMachineConfigurationData : TypedPropertyBag
{
    public const string TIMEOUT_KEY = "Timeout";
    public const string RETRY_COUNT_KEY = "RetryCount";
    public const string PRIORITY_KEY = "Priority";
    public const string CUSTOM_SETTINGS_KEY = "CustomSettings";
    public const string VALIDATION_RULES_KEY = "ValidationRules";
    public const string EVENT_HANDLERS_KEY = "EventHandlers";

    public TimeSpan? Timeout
    {
        get => Get<TimeSpan>(TIMEOUT_KEY);
        set
        {
            if (value != null)
            {
                Set(TIMEOUT_KEY, value.Value);
            }
            else
            {
                Remove(TIMEOUT_KEY);
            }
        }
    }

    public int? RetryCount
    {
        get => Get<int>(RETRY_COUNT_KEY);
        set
        {
            if (value != null)
            {
                Set(RETRY_COUNT_KEY, value.Value);
            }
            else
            {
                Remove(RETRY_COUNT_KEY);
            }
        }
    }

    public int? Priority
    {
        get => Get<int>(PRIORITY_KEY);
        set
        {
            if (value != null)
            {
                Set(PRIORITY_KEY, value.Value);
            }
            else
            {
                Remove(PRIORITY_KEY);
            }
        }
    }

    public string? CustomSettings
    {
        get => Get<string>(CUSTOM_SETTINGS_KEY);
        set
        {
            if (value != null)
            {
                Set(CUSTOM_SETTINGS_KEY, value);
            }
            else
            {
                Remove(CUSTOM_SETTINGS_KEY);
            }
        }
    }

    public string? ValidationRules
    {
        get => Get<string>(VALIDATION_RULES_KEY);
        set
        {
            if (value != null)
            {
                Set(VALIDATION_RULES_KEY, value);
            }
            else
            {
                Remove(VALIDATION_RULES_KEY);
            }
        }
    }

    public string? EventHandlers
    {
        get => Get<string>(EVENT_HANDLERS_KEY);
        set
        {
            if (value != null)
            {
                Set(EVENT_HANDLERS_KEY, value);
            }
            else
            {
                Remove(EVENT_HANDLERS_KEY);
            }
        }
    }
}
