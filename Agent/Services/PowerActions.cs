using System;
using System.Diagnostics;
using System.IO;

namespace Remotely.Agent.Services;

/// <summary>Reiniciar ou desligar o computador com aviso ao usuário (pedido pelo painel).</summary>
internal static class PowerActions
{
    public static (bool Success, string Output) Execute(string action, int delaySeconds)
    {
        var restart = action == "restart";
        if (!restart && action != "shutdown")
        {
            return (false, "Ação inválida.");
        }

        delaySeconds = Math.Clamp(delaySeconds, 0, 3600);
        var what = restart ? "reiniciar" : "desligar";
        var when = delaySeconds >= 60 ? $"em {delaySeconds / 60} minuto(s)" : delaySeconds > 0 ? $"em {delaySeconds} segundos" : "agora";
        var message = $"O suporte de TI vai {what} este computador {when}. Salve seu trabalho.";

        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                var shutdown = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32",
                    "shutdown.exe");
                psi = new ProcessStartInfo(shutdown);
                psi.ArgumentList.Add(restart ? "/r" : "/s");
                psi.ArgumentList.Add("/t");
                psi.ArgumentList.Add(delaySeconds.ToString());
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(message);
                psi.ArgumentList.Add("/d");
                psi.ArgumentList.Add("p:4:1");
            }
            else
            {
                psi = new ProcessStartInfo("shutdown");
                psi.ArgumentList.Add(restart ? "-r" : "-h");
                psi.ArgumentList.Add(delaySeconds == 0 ? "now" : $"+{Math.Max(1, (int)Math.Ceiling(delaySeconds / 60.0))}");
                psi.ArgumentList.Add(message);
            }

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (false, "Não foi possível iniciar o comando.");
            }
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(15_000);
            return (process.ExitCode == 0, output.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
