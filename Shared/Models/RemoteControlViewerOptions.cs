using System.Runtime.Serialization;

namespace Remotely.Shared.Models;

[DataContract]
public class RemoteControlViewerOptions
{
    [DataMember]
    public bool ShouldRecordSession { get; init; }

    // Permissões do técnico nesta sessão (perfil de acesso). O servidor também as confere.
    [DataMember]
    public bool AllowInput { get; init; } = true;

    [DataMember]
    public bool AllowFiles { get; init; } = true;

    [DataMember]
    public bool AllowClipboard { get; init; } = true;

    [DataMember]
    public bool AllowSystemActions { get; init; } = true;

    /// <summary>Minutos sem mouse ou teclado até a sessão ser encerrada (0 = nunca).</summary>
    [DataMember]
    public int IdleTimeoutMinutes { get; init; }

    /// <summary>Computador da sessão aberta pelo painel (para o painel lateral do visualizador).</summary>
    [DataMember]
    public string? DeviceId { get; init; }

    [DataMember]
    public string? DeviceName { get; init; }

    /// <summary>Windows, Linux... (o modo de segurança só aparece no Windows).</summary>
    [DataMember]
    public string? DevicePlatform { get; init; }
}
