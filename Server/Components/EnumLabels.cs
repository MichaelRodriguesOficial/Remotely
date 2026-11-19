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
            _ => $"{value}"
        };
    }
}
