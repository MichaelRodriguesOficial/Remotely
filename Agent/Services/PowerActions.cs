using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;

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
            using (var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey($@"{SafeBootNetworkKey}\{ServiceName}"))
            {
                key.SetValue(string.Empty, "Service");
            }

            // O Windows não liga o Wi-Fi no modo de segurança com rede: só a rede cabeada. Com o
            // computador no Wi-Fi, o serviço de Wi-Fi, as dependências e o driver da placa entram na lista.
            var added = new List<string>();
            var wifi = WifiAdapterServices();
            if (wifi is not null)
            {
                foreach (var service in WifiServices)
                {
                    RegisterSafeBootEntry(service, "Service", added);
                }
                foreach (var driver in WifiDrivers.Concat(wifi))
                {
                    RegisterSafeBootEntry(driver, "Driver", added);
                }
            }

            var (ok, output) = RunTool(SystemTool("bcdedit.exe"), "/set", "{current}", "safeboot", "network");
            if (!ok)
            {
                RemoveSafeBootEntries(added);
                return (false, $"bcdedit falhou: {output}");
            }

            // Linha 1: quando foi pedido. Demais: entradas criadas agora, retiradas quando o computador voltar ao normal.
            File.WriteAllLines(SafeBootMarker, new[] { DateTimeOffset.Now.ToString("O") }.Concat(added.Select(x => $"added:{x}")));
            return (true, wifi is null
                ? output
                : $"{output} Wi-Fi preparado para o modo de segurança ({string.Join(", ", added)}).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Chamado na inicialização do agente. No modo de segurança: tira a marca do próximo boot (o reinício
    /// seguinte já volta ao normal). De volta ao modo normal: retira as entradas de Wi-Fi criadas para o
    /// modo de segurança e apaga o marcador.
    /// </summary>
    public static void ClearPendingSafeBoot(Microsoft.Extensions.Logging.ILogger logger)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(SafeBootMarker))
        {
            return;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(SafeBootMarker);
        }
        catch
        {
            lines = [];
        }

        if (!lines.Contains(FlagCleared))
        {
            var (ok, output) = RunTool(SystemTool("bcdedit.exe"), "/deletevalue", "{current}", "safeboot");
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
                "Safe boot flag removed after restart in safe mode. Success: {success}. {output}", ok, output);
            if (!ok && !output.Contains("not found", StringComparison.OrdinalIgnoreCase) &&
                !output.Contains("não", StringComparison.OrdinalIgnoreCase))
            {
                return; // Tenta de novo no próximo início.
            }

            lines = [.. lines, FlagCleared];
        }

        var bootMode = GetSystemMetrics(SM_CLEANBOOT);
        if (bootMode != 0)
        {
            // Ainda no modo de segurança: as entradas de Wi-Fi continuam até o computador voltar ao normal.
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
                "Running in safe mode ({mode}).", bootMode == 2 ? "with networking" : "minimal");
            TryWrite(lines);
            return;
        }

        var added = lines.Where(x => x.StartsWith("added:", StringComparison.Ordinal)).Select(x => x[6..]).ToList();
        RemoveSafeBootEntries(added);
        if (added.Count > 0)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
                "Safe mode Wi-Fi entries removed: {entries}.", string.Join(", ", added));
        }

        try
        {
            File.Delete(SafeBootMarker);
        }
        catch
        {
            // Tenta de novo no próximo início.
        }

        static void TryWrite(string[] content)
        {
            try
            {
                File.WriteAllLines(SafeBootMarker, content);
            }
            catch
            {
                // Sem o registro, a marca do boot já saiu; as entradas de Wi-Fi ficam (não fazem mal).
            }
        }
    }

    private const string FlagCleared = "flag-cleared";
    private const string SafeBootNetworkKey = @"SYSTEM\CurrentControlSet\Control\SafeBoot\Network";
    private const int SM_CLEANBOOT = 67;

    /// <summary>Serviço de Wi-Fi e o que ele precisa (EapHost: Wi-Fi corporativo com 802.1X).</summary>
    private static readonly string[] WifiServices = ["WlanSvc", "Wcmsvc", "EapHost"];

    /// <summary>Drivers da pilha de Wi-Fi do Windows.</summary>
    private static readonly string[] WifiDrivers = ["NativeWifiP", "Ndisuio", "vwififlt", "vwifibus"];

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    /// <summary>
    /// Drivers das placas Wi-Fi conectadas, ou nulo quando o computador não está no Wi-Fi
    /// (ou já tem uma rede cabeada ligada, que funciona no modo de segurança sem nada a mais).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string>? WifiAdapterServices()
    {
        var up = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                        x.GetIPProperties().GatewayAddresses.Count > 0)
            .ToList();

        if (up.Any(x => x.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet))
        {
            return null;
        }

        var wifi = up.Where(x => x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211).ToList();
        if (wifi.Count == 0)
        {
            return null;
        }

        var services = new List<string>();
        const string netClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
        using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(netClass);
        if (classKey is null)
        {
            return services;
        }

        foreach (var name in classKey.GetSubKeyNames())
        {
            try
            {
                using var adapter = classKey.OpenSubKey(name);
                var instanceId = adapter?.GetValue("NetCfgInstanceId") as string;
                if (instanceId is null || !wifi.Any(x => string.Equals(x.Id, instanceId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var service = adapter?.GetValue("Service") as string;
                if (string.IsNullOrWhiteSpace(service) && adapter?.GetValue("DeviceInstanceID") is string deviceId)
                {
                    using var device = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{deviceId}");
                    service = device?.GetValue("Service") as string;
                }

                if (!string.IsNullOrWhiteSpace(service))
                {
                    services.Add(service);
                }
            }
            catch
            {
                // Sem acesso a uma placa: segue com as outras.
            }
        }

        return services;
    }

    /// <summary>Cria a entrada só se ela ainda não existe (as do Windows não são tocadas).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RegisterSafeBootEntry(string name, string type, List<string> added)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || added.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        using var existing = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"{SafeBootNetworkKey}\{name}");
        if (existing is not null)
        {
            return;
        }

        using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey($@"{SafeBootNetworkKey}\{name}");
        key.SetValue(string.Empty, type);
        added.Add(name);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RemoveSafeBootEntries(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || string.Equals(name, ServiceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree($@"{SafeBootNetworkKey}\{name}", throwOnMissingSubKey: false);
            }
            catch
            {
                // Fica para a próxima limpeza.
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
