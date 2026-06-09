namespace Atelier.Framework.Properties;

public class StateMachineConfiguration : TypedPropertyBag
{
    public const string INITIAL_STATE_KEY = "InitialState";
    public const string PERSIST_KEY = "Persist";
    public const string MONITOR_KEY = "Monitor";
    public const string AUTO_CLEANUP_TIMEOUT_KEY = "AutoCleanupTimeout";
    public const string MAX_TRANSITIONS_PER_MINUTE_KEY = "MaxTransitionsPerMinute";
    public const string ENABLE_EVENTS_KEY = "EnableEvents";

    public string? InitialState
    {
        get => Get<string>(INITIAL_STATE_KEY);
        set
        {
            if (value != null)
            {
                Set(INITIAL_STATE_KEY, value);
            }
            else
            {
                Remove(INITIAL_STATE_KEY);
            }
        }
    }

    public bool? Persist
    {
        get => Get<bool>(PERSIST_KEY);
        set
        {
            if (value != null)
            {
                Set(PERSIST_KEY, value.Value);
            }
            else
            {
                Remove(PERSIST_KEY);
            }
        }
    }

    public bool? Monitor
    {
        get => Get<bool>(MONITOR_KEY);
        set
        {
            if (value != null)
            {
                Set(MONITOR_KEY, value.Value);
            }
            else
            {
                Remove(MONITOR_KEY);
            }
        }
    }

    public TimeSpan? AutoCleanupTimeout
    {
        get => Get<TimeSpan>(AUTO_CLEANUP_TIMEOUT_KEY);
        set
        {
            if (value != null)
            {
                Set(AUTO_CLEANUP_TIMEOUT_KEY, value.Value);
            }
            else
            {
                Remove(AUTO_CLEANUP_TIMEOUT_KEY);
            }
        }
    }

    public int? MaxTransitionsPerMinute
    {
        get => Get<int>(MAX_TRANSITIONS_PER_MINUTE_KEY);
        set
        {
            if (value != null)
            {
                Set(MAX_TRANSITIONS_PER_MINUTE_KEY, value.Value);
            }
            else
            {
                Remove(MAX_TRANSITIONS_PER_MINUTE_KEY);
            }
        }
    }

    public bool? EnableEvents
    {
        get => Get<bool>(ENABLE_EVENTS_KEY);
        set
        {
            if (value != null)
            {
                Set(ENABLE_EVENTS_KEY, value.Value);
            }
            else
            {
                Remove(ENABLE_EVENTS_KEY);
            }
        }
    }
}
