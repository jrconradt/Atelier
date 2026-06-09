namespace Atelier.Framework.Properties;

public class ServiceMetadata : TypedPropertyBag
{
    public const string SERVICE_NAME_KEY = "ServiceName";
    public const string SERVICE_VERSION_KEY = "ServiceVersion";
    public const string INSTANCE_ID_KEY = "InstanceId";
    public const string ENVIRONMENT_KEY = "Environment";
    public const string REGION_KEY = "Region";
    public const string ZONE_KEY = "Zone";
    public const string HOST_NAME_KEY = "HostName";
    public const string PROCESS_ID_KEY = "ProcessId";

    public string? ServiceName
    {
        get => Get<string>(SERVICE_NAME_KEY);
        set
        {
            if (value != null)
            {
                Set(SERVICE_NAME_KEY, value);
            }
            else
            {
                Remove(SERVICE_NAME_KEY);
            }
        }
    }

    public string? ServiceVersion
    {
        get => Get<string>(SERVICE_VERSION_KEY);
        set
        {
            if (value != null)
            {
                Set(SERVICE_VERSION_KEY, value);
            }
            else
            {
                Remove(SERVICE_VERSION_KEY);
            }
        }
    }

    public string? InstanceId
    {
        get => Get<string>(INSTANCE_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(INSTANCE_ID_KEY, value);
            }
            else
            {
                Remove(INSTANCE_ID_KEY);
            }
        }
    }

    public string? Environment
    {
        get => Get<string>(ENVIRONMENT_KEY);
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

    public string? Region
    {
        get => Get<string>(REGION_KEY);
        set
        {
            if (value != null)
            {
                Set(REGION_KEY, value);
            }
            else
            {
                Remove(REGION_KEY);
            }
        }
    }

    public string? Zone
    {
        get => Get<string>(ZONE_KEY);
        set
        {
            if (value != null)
            {
                Set(ZONE_KEY, value);
            }
            else
            {
                Remove(ZONE_KEY);
            }
        }
    }

    public string? HostName
    {
        get => Get<string>(HOST_NAME_KEY);
        set
        {
            if (value != null)
            {
                Set(HOST_NAME_KEY, value);
            }
            else
            {
                Remove(HOST_NAME_KEY);
            }
        }
    }

    public string? ProcessId
    {
        get => Get<string>(PROCESS_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(PROCESS_ID_KEY, value);
            }
            else
            {
                Remove(PROCESS_ID_KEY);
            }
        }
    }
}
