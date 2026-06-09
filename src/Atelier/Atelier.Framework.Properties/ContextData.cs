namespace Atelier.Framework.Properties;

public class ContextData : TypedPropertyBag
{
    public const string USER_ID_KEY = "UserId";
    public const string SESSION_ID_KEY = "SessionId";
    public const string TENANT_ID_KEY = "TenantId";
    public const string REQUEST_ID_KEY = "RequestId";
    public const string CLIENT_ID_KEY = "ClientId";
    public const string IP_ADDRESS_KEY = "IpAddress";
    public const string USER_AGENT_KEY = "UserAgent";
    public const string LOCALE_KEY = "Locale";
    public const string TIMEZONE_KEY = "Timezone";

    public string? UserId
    {
        get => Get<string>(USER_ID_KEY);
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
        get => Get<string>(SESSION_ID_KEY);
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
        get => Get<string>(TENANT_ID_KEY);
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
        get => Get<string>(REQUEST_ID_KEY);
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

    public string? ClientId
    {
        get => Get<string>(CLIENT_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(CLIENT_ID_KEY, value);
            }
            else
            {
                Remove(CLIENT_ID_KEY);
            }
        }
    }

    public string? IpAddress
    {
        get => Get<string>(IP_ADDRESS_KEY);
        set
        {
            if (value != null)
            {
                Set(IP_ADDRESS_KEY, value);
            }
            else
            {
                Remove(IP_ADDRESS_KEY);
            }
        }
    }

    public string? UserAgent
    {
        get => Get<string>(USER_AGENT_KEY);
        set
        {
            if (value != null)
            {
                Set(USER_AGENT_KEY, value);
            }
            else
            {
                Remove(USER_AGENT_KEY);
            }
        }
    }

    public string? Locale
    {
        get => Get<string>(LOCALE_KEY);
        set
        {
            if (value != null)
            {
                Set(LOCALE_KEY, value);
            }
            else
            {
                Remove(LOCALE_KEY);
            }
        }
    }

    public string? Timezone
    {
        get => Get<string>(TIMEZONE_KEY);
        set
        {
            if (value != null)
            {
                Set(TIMEZONE_KEY, value);
            }
            else
            {
                Remove(TIMEZONE_KEY);
            }
        }
    }
}
