namespace Remotely.Server.Models;

/// <summary>O que o técnico pode fazer numa sessão de controle remoto (vem do perfil de acesso).</summary>
/// <param name="Input">Controlar mouse e teclado (sem isso, só vê a tela).</param>
/// <param name="Files">Enviar e baixar arquivos.</param>
/// <param name="Clipboard">Área de transferência e "digitar" texto.</param>
/// <param name="SystemActions">Ctrl+Alt+Del, bloquear teclado e mouse, trocar a sessão do Windows.</param>
public sealed record RemoteControlPermissions(bool Input, bool Files, bool Clipboard, bool SystemActions)
{
    public static RemoteControlPermissions Full { get; } = new(true, true, true, true);
    public static RemoteControlPermissions None { get; } = new(false, false, false, false);
}
