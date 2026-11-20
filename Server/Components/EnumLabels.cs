using Remotely.Shared.Enums;

namespace Remotely.Server.Components;

/// <summary>
/// Rótulos em português para enums exibidos na interface.
/// O valor do enum (usado no binding) não muda; só o texto exibido.
/// </summary>
public static class EnumLabels
{
    public static string Get(object? value)
    {
        return value switch
        {
            Theme.Dark => "Escuro",
            Theme.Light => "Claro",
            RepeatInterval.Hourly => "A cada hora",
            RepeatInterval.Daily => "Diariamente",
            RepeatInterval.Weekly => "Semanalmente",
            RepeatInterval.Monthly => "Mensalmente",
            ScriptingShell.PSCore => "PowerShell 7",
            ScriptingShell.WinPS => "Windows PowerShell",
            ScriptingShell.Bash => "Bash",
            ScriptingShell.CMD => "CMD",
            _ => $"{value}"
        };
    }

    /// <summary>
    /// Rótulo curto do shell, usado em etiquetas.
    /// </summary>
    public static string ShortShell(ScriptingShell shell)
    {
        return shell switch
        {
            ScriptingShell.PSCore => "PS 7",
            ScriptingShell.WinPS => "PS 5",
            ScriptingShell.Bash => "Bash",
            ScriptingShell.CMD => "CMD",
            _ => $"{shell}"
        };
    }
}
