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

    /// <summary>Tela preta: "Allow" (o técnico liga), "Auto" (liga ao conectar) ou "Deny".</summary>
    [DataMember]
    public string PrivacyScreen { get; init; } = "Allow";

    /// <summary>GLPI configurado: ao encerrar, a anotação pode virar chamado ou acompanhamento.</summary>
    [DataMember]
    public bool GlpiEnabled { get; init; }

    /// <summary>Sessão registrada no histórico: ao encerrar, o técnico pode anotar o que fez.</summary>
    [DataMember]
    public bool AllowNote { get; init; }

    /// <summary>Pelo inventário, o computador está só no Wi-Fi (sem cabo): o modo de segurança pode ficar sem rede.</summary>
    [DataMember]
    public bool DeviceOnWifi { get; init; }
}
