using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;

/// <summary>Tela preta: pedido do visualizador e resposta do computador.</summary>
[DataContract]
public class PrivacyScreenDto
{
    [DataMember(Name = "Enabled")]
    public bool Enabled { get; set; }

    [DataMember(Name = "Supported")]
    public bool Supported { get; set; } = true;

    [DataMember(Name = "Message")]
    public string? Message { get; set; }
}
