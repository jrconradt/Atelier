namespace Atelier.Framework.Properties;

public class StateMachineData : TypedPropertyBag
{
    public const string CURRENT_STATE_KEY = "CurrentState";
    public const string PREVIOUS_STATE_KEY = "PreviousState";
    public const string TRANSITION_COUNT_KEY = "TransitionCount";
    public const string LAST_TRANSITION_TIME_KEY = "LastTransitionTime";
    public const string ERROR_COUNT_KEY = "ErrorCount";
    public const string IS_ACTIVE_KEY = "IsActive";
    public const string CONTEXT_DATA_KEY = "ContextData";
    public const string CUSTOM_DATA_KEY = "CustomData";

    public string? CurrentState
    {
        get => Get<string>(CURRENT_STATE_KEY);
        set
        {
            if (value != null)
            {
                Set(CURRENT_STATE_KEY, value);
            }
            else
            {
                Remove(CURRENT_STATE_KEY);
            }
        }
    }

    public string? PreviousState
    {
        get => Get<string>(PREVIOUS_STATE_KEY);
        set
        {
            if (value != null)
            {
                Set(PREVIOUS_STATE_KEY, value);
            }
            else
            {
                Remove(PREVIOUS_STATE_KEY);
            }
        }
    }

    public int? TransitionCount
    {
        get => Get<int>(TRANSITION_COUNT_KEY);
        set
        {
            if (value != null)
            {
                Set(TRANSITION_COUNT_KEY, value.Value);
            }
            else
            {
                Remove(TRANSITION_COUNT_KEY);
            }
        }
    }

    public DateTime? LastTransitionTime
    {
        get => Get<DateTime>(LAST_TRANSITION_TIME_KEY);
        set
        {
            if (value != null)
            {
                Set(LAST_TRANSITION_TIME_KEY, value.Value);
            }
            else
            {
                Remove(LAST_TRANSITION_TIME_KEY);
            }
        }
    }

    public int? ErrorCount
    {
        get => Get<int>(ERROR_COUNT_KEY);
        set
        {
            if (value != null)
            {
                Set(ERROR_COUNT_KEY, value.Value);
            }
            else
            {
                Remove(ERROR_COUNT_KEY);
            }
        }
    }

    public bool? IsActive
    {
        get => Get<bool>(IS_ACTIVE_KEY);
        set
        {
            if (value != null)
            {
                Set(IS_ACTIVE_KEY, value.Value);
            }
            else
            {
                Remove(IS_ACTIVE_KEY);
            }
        }
    }

    public string? ContextData
    {
        get => Get<string>(CONTEXT_DATA_KEY);
        set
        {
            if (value != null)
            {
                Set(CONTEXT_DATA_KEY, value);
            }
            else
            {
                Remove(CONTEXT_DATA_KEY);
            }
        }
    }

    public object? CustomData
    {
        get => Get<object>(CUSTOM_DATA_KEY);
        set
        {
            if (value != null)
            {
                Set(CUSTOM_DATA_KEY, value);
            }
            else
            {
                Remove(CUSTOM_DATA_KEY);
            }
        }
    }
}
