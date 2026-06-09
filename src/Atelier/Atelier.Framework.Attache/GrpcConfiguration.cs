using Atelier.Framework.Attributes;

namespace Atelier.Framework.Attache;

[Contract("GrpcConfiguration", Version = "1.0", Namespace = "Framework.Attache")]
public class GrpcConfiguration
{
    public int MaxReceiveMessageSizeBytes { get; set; } = 50 * 1024 * 1024;
    public int MaxSendMessageSizeBytes { get; set; } = 50 * 1024 * 1024;
}
