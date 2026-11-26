using Remotely.Server.Hubs;
using Remotely.Server.Services;
using Remotely.Shared.Entities;

namespace Remotely.Server.Components.Devices;

/// <summary>Confirma e envia "Reiniciar" ou "Desligar" para um ou mais computadores.</summary>
public static class PowerActionPrompt
{
    /// <summary>Aviso na tela do usuário antes de reiniciar/desligar.</summary>
    public const int DelaySeconds = 60;

    public static async Task Run(
        IJsInterop jsInterop,
        ICircuitConnection circuitConnection,
        IToastService toastService,
        IReadOnlyCollection<Device> devices,
        bool restart)
    {
        var online = devices.Where(x => x.IsOnline).ToList();
        if (online.Count == 0)
        {
            toastService.ShowToast2("Nenhum dos computadores está online.", Enums.ToastType.Warning);
            return;
        }

        var offline = devices.Count - online.Count;
        var target = online.Count == 1 ? online[0].DeviceName : $"{online.Count} computadores";
        var confirmed = await jsInterop.Confirm(new ConfirmOptions(
            restart ? $"Reiniciar {target}" : $"Desligar {target}",
            $"O usuário vê um aviso na tela e o computador {(restart ? "reinicia" : "desliga")} em 1 minuto. Trabalho não salvo é perdido." +
            (restart ? "" : " Para ligar de novo, use o Wake-on-LAN (se a placa de rede permitir) ou alguém no local.") +
            (offline > 0 ? $" {offline} offline fica(m) de fora." : ""),
            restart ? "Reiniciar" : "Desligar",
            Danger: true,
            Icon: restart ? "fa-solid fa-rotate-right" : "fa-solid fa-power-off"));
        if (!confirmed)
        {
            return;
        }

        var result = await circuitConnection.PowerAction(
            online.Select(x => x.ID).ToArray(),
            restart ? "restart" : "shutdown",
            DelaySeconds);
        if (!result.IsSuccess)
        {
            toastService.ShowToast2(result.Reason, Enums.ToastType.Warning);
            return;
        }

        toastService.ShowToast2(
            $"{(restart ? "Reinício" : "Desligamento")} enviado para {result.Value} computador(es). Acontece em 1 minuto.",
            Enums.ToastType.Success);
    }
}
