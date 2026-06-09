namespace Atelier.Framework.Properties;

public class NetworkMetadata : TypedPropertyBag
{
    public const string ZONE_ID_KEY = "ZoneId";
    public const string SECURITY_LEVEL_KEY = "SecurityLevel";
    public const string NETWORK_TYPE_KEY = "NetworkType";
    public const string IP_ADDRESS_KEY = "IpAddress";
    public const string SUBNET_MASK_KEY = "SubnetMask";
    public const string GATEWAY_KEY = "Gateway";
    public const string DNS_SERVERS_KEY = "DnsServers";
    public const string VLAN_ID_KEY = "VlanId";
    public const string PRIORITY_KEY = "Priority";
    public const string CUSTOM_METADATA_KEY = "CustomMetadata";

    public string? ZoneId
    {
        get => Get<string>(ZONE_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(ZONE_ID_KEY, value);
            }
            else
            {
                Remove(ZONE_ID_KEY);
            }
        }
    }

    public string? SecurityLevel
    {
        get => Get<string>(SECURITY_LEVEL_KEY);
        set
        {
            if (value != null)
            {
                Set(SECURITY_LEVEL_KEY, value);
            }
            else
            {
                Remove(SECURITY_LEVEL_KEY);
            }
        }
    }

    public string? NetworkType
    {
        get => Get<string>(NETWORK_TYPE_KEY);
        set
        {
            if (value != null)
            {
                Set(NETWORK_TYPE_KEY, value);
            }
            else
            {
                Remove(NETWORK_TYPE_KEY);
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

    public string? SubnetMask
    {
        get => Get<string>(SUBNET_MASK_KEY);
        set
        {
            if (value != null)
            {
                Set(SUBNET_MASK_KEY, value);
            }
            else
            {
                Remove(SUBNET_MASK_KEY);
            }
        }
    }

    public string? Gateway
    {
        get => Get<string>(GATEWAY_KEY);
        set
        {
            if (value != null)
            {
                Set(GATEWAY_KEY, value);
            }
            else
            {
                Remove(GATEWAY_KEY);
            }
        }
    }

    public string? DnsServers
    {
        get => Get<string>(DNS_SERVERS_KEY);
        set
        {
            if (value != null)
            {
                Set(DNS_SERVERS_KEY, value);
            }
            else
            {
                Remove(DNS_SERVERS_KEY);
            }
        }
    }

    public int? VlanId
    {
        get => Get<int>(VLAN_ID_KEY);
        set
        {
            if (value != null)
            {
                Set(VLAN_ID_KEY, value.Value);
            }
            else
            {
                Remove(VLAN_ID_KEY);
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

    public string? CustomMetadata
    {
        get => Get<string>(CUSTOM_METADATA_KEY);
        set
        {
            if (value != null)
            {
                Set(CUSTOM_METADATA_KEY, value);
            }
            else
            {
                Remove(CUSTOM_METADATA_KEY);
            }
        }
    }
}
