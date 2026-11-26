using MessagePack;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Remotely.Shared.Models;

[DataContract]
public class RemoteControlAccessRequest
{
    [JsonConstructor]
    [SerializationConstructor]
    public RemoteControlAccessRequest(string viewerConnectionId, string requesterDisplayName, string organizationName, int autoAcceptSeconds = 0)
    {
        ViewerConnectionId = viewerConnectionId;
        RequesterDisplayName = requesterDisplayName;
        OrganizationName = organizationName;
        AutoAcceptSeconds = autoAcceptSeconds;
    }

    /// <summary>Se maior que zero, o pedido é aceito sozinho quando o usuário não responde nesse tempo.</summary>
    [DataMember]
    public int AutoAcceptSeconds { get; }

    [DataMember]
    public string OrganizationName { get; }

    [DataMember]
    public string RequesterDisplayName { get; }

    [DataMember]
    public string ViewerConnectionId { get; }
}
