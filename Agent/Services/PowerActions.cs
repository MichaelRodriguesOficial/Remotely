using System;
using System.Diagnostics;
using System.IO;

namespace Remotely.Agent.Services;

/// <summary>Reiniciar ou desligar o computador com aviso ao usuário (pedido pelo painel).</summary>
internal static class PowerActions
{
    public static (bool Success, string Output) Execute(string action, int delaySeconds)
    {
        if (action == RestartSafeMode)
        {
            if (!OperatingSystem.IsWindows())
            {
                return (false, "Modo de segurança só existe no Windows.");
            }
            var (prepared, prepareOutput) = PrepareSafeBoot();
            if (!prepared)
            {
                return (false, prepareOutput);
            }
            action = "restart";
        }

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

    /// <summary>Reiniciar em modo de segurança com rede (só Windows), pedido no visualizador.</summary>
    public const string RestartSafeMode = "restart-safe";

    private const string ServiceName = "Remotely_Service";
    private static string SafeBootMarker => Path.Combine(AppContext.BaseDirectory, "safeboot.pending");

    private static string SystemTool(string exe) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32",
        exe);

    /// <summary>
    /// Liga o agente no modo de segurança com rede (chave SafeBoot\Network) e marca o próximo boot
    /// como modo de segurança. A marca é retirada pelo agente assim que ele inicia (<see cref="ClearPendingSafeBoot"/>),
    /// então só um reinício cai no modo de segurança: o seguinte já volta ao normal.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static (bool Success, string Output) PrepareSafeBoot()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                $@"SYSTEM\CurrentControlSet\Control\SafeBoot\Network\{ServiceName}"))
            {
                key.SetValue(string.Empty, "Service");
            }

            var (ok, output) = RunTool(SystemTool("bcdedit.exe"), "/set", "{current}", "safeboot", "network");
            if (!ok)
            {
                return (false, $"bcdedit falhou: {output}");
            }

            File.WriteAllText(SafeBootMarker, DateTimeOffset.Now.ToString("O"));
            return (true, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Chamado na inicialização do agente: desfaz o modo de segurança agendado pelo painel.</summary>
    public static void ClearPendingSafeBoot(Microsoft.Extensions.Logging.ILogger logger)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(SafeBootMarker))
        {
            return;
        }

        var (ok, output) = RunTool(SystemTool("bcdedit.exe"), "/deletevalue", "{current}", "safeboot");
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
            "Safe boot flag removed after restart in safe mode. Success: {success}. {output}", ok, output);
        if (ok || output.Contains("not found", StringComparison.OrdinalIgnoreCase) || output.Contains("não", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(SafeBootMarker);
            }
            catch
            {
                // Tenta de novo no próximo início.
            }
        }
    }

    private static (bool Success, string Output) RunTool(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
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
