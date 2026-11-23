namespace Remotely.Server.Models;

/// <summary>
/// Perfil de acesso de uma organização. Define o que os usuários do perfil podem fazer
/// e, opcionalmente, quais grupos de dispositivos eles enxergam.
/// Administradores da organização não são afetados por perfis.
/// </summary>
public class AccessProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Controle remoto, somente visualizar e chat.</summary>
    public bool AllowRemoteControl { get; set; } = true;

    /// <summary>Terminal, executar, criar e agendar scripts.</summary>
    public bool AllowScripts { get; set; } = true;

    /// <summary>Editar dispositivos, enviar arquivos, ligar (WoL), desinstalar e remover agentes.</summary>
    public bool AllowDeviceManagement { get; set; } = true;

    /// <summary>
    /// Executar os reparos da aba Diagnóstico (reiniciar, verificar disco, limpar, redefinir rede etc.).
    /// Desligado por padrão: perfis criados antes desta opção não ganham a permissão sozinhos.
    /// </summary>
    public bool AllowRepairs { get; set; }

    /// <summary>
    /// Quando ligado, os usuários do perfil passam a enxergar só os grupos em <see cref="DeviceGroupIds"/>.
    /// </summary>
    public bool RestrictToGroups { get; set; }

    public List<string> DeviceGroupIds { get; set; } = new();

    public List<string> UserIds { get; set; } = new();

    public AccessProfile Clone()
    {
        var clone = (AccessProfile)MemberwiseClone();
        clone.DeviceGroupIds = new List<string>(DeviceGroupIds);
        clone.UserIds = new List<string>(UserIds);
        return clone;
    }
}

/// <summary>
/// Permissões efetivas de um usuário depois de aplicar o perfil.
/// </summary>
public record UserPermissions(
    bool RemoteControl,
    bool Scripts,
    bool ManageDevices,
    bool Repairs,
    string? ProfileName)
{
    public static UserPermissions Full { get; } = new(true, true, true, true, null);
}

public enum AccessPermission
{
    RemoteControl,
    Scripts,
    ManageDevices,
    Repairs
}
