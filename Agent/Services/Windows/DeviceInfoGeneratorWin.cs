using Microsoft.Extensions.Logging;
using Remotely.Agent.Interfaces;
using Remotely.Shared.Dtos;
using System;
using System.Linq;
using System.Threading.Tasks;
using Remotely.Desktop.Native.Windows;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Remotely.Agent.Services.Windows;

public class DeviceInfoGeneratorWin : DeviceInfoGeneratorBase, IDeviceInformationService
{
    private readonly ICpuUtilizationSampler _cpuUtilSampler;

    public DeviceInfoGeneratorWin(
        ICpuUtilizationSampler cpuUtilSampler, 
        ILogger<DeviceInfoGeneratorWin> logger)
        : base(logger)
    { 
        _cpuUtilSampler = cpuUtilSampler;
    }

    public Task<DeviceClientDto> CreateDevice(string deviceId, string orgId)
    {
        var device = GetDeviceBase(deviceId, orgId);

        try
        {
            var (usedStorage, totalStorage) = GetSystemDriveInfo();
            var (usedMemory, totalMemory) = GetMemoryInGB();

            var userName = Win32Interop.GetActiveSessions().LastOrDefault()?.Username ?? string.Empty;
            device.CurrentUser = FormatUserWithDisplayName(userName, GetDisplayName(userName));
            device.OSDescription = GetFriendlyOsDescription() ?? device.OSDescription;
            device.Drives = GetAllDrives();
            device.UsedStorage = usedStorage;
            device.TotalStorage = totalStorage;
            device.UsedMemory = usedMemory;
            device.TotalMemory = totalMemory;
            device.CpuUtilization = _cpuUtilSampler.CurrentUtilization;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device info.");
        }

        return Task.FromResult(device);
    }

    /// <summary>
    /// "Microsoft Windows 11 Enterprise (10.0.26300)": nome comercial com edição, e a versão
    /// entre parênteses (o painel mostra as duas partes separadas).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private string? GetFriendlyOsDescription()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName") as string;
            if (string.IsNullOrWhiteSpace(productName))
            {
                return null;
            }

            var osVersion = Environment.OSVersion.Version;

            // O registro continua dizendo "Windows 10" no Windows 11; o build 22000 marca o 11.
            if (osVersion.Build >= 22000)
            {
                productName = productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
            }

            if (!productName.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
            {
                productName = $"Microsoft {productName}";
            }

            var version = $"{osVersion.Major}.{osVersion.Minor}.{osVersion.Build}";
            var displayVersion = key?.GetValue("DisplayVersion") as string;
            return string.IsNullOrWhiteSpace(displayVersion)
                ? $"{productName} ({version})"
                : $"{productName} ({version} · {displayVersion})";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while reading the Windows product name.");
            return null;
        }
    }

    /// <summary>
    /// Nome de exibição (nome e sobrenome) do usuário logado, lido dos dados de sessão do
    /// Windows (funciona com contas locais, do domínio e do Entra ID, sem consultar o AD).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private string? GetDisplayName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        try
        {
            static bool SameUser(string? loggedOnUser, string userName)
            {
                if (string.IsNullOrWhiteSpace(loggedOnUser))
                {
                    return false;
                }
                var account = loggedOnUser.Split('\\').Last();
                return account.Equals(userName, StringComparison.OrdinalIgnoreCase) ||
                    account.Split('@').First().Equals(userName, StringComparison.OrdinalIgnoreCase);
            }

            using var logonUi = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI");
            if (logonUi is null)
            {
                return null;
            }

            using (var sessionData = logonUi.OpenSubKey("SessionData"))
            {
                if (sessionData is not null)
                {
                    foreach (var sessionId in sessionData.GetSubKeyNames())
                    {
                        using var session = sessionData.OpenSubKey(sessionId);
                        if (SameUser(session?.GetValue("LoggedOnUser") as string, userName) &&
                            session?.GetValue("LoggedOnDisplayName") is string displayName &&
                            !string.IsNullOrWhiteSpace(displayName))
                        {
                            return displayName.Trim();
                        }
                    }
                }
            }

            if (SameUser(logonUi.GetValue("LastLoggedOnUser") as string, userName) &&
                logonUi.GetValue("LastLoggedOnDisplayName") is string lastDisplayName &&
                !string.IsNullOrWhiteSpace(lastDisplayName))
            {
                return lastDisplayName.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while reading the display name of the logged on user.");
        }

        return null;
    }

    public (double usedGB, double totalGB) GetMemoryInGB()
    {
        try
        {
            var memoryStatus = new Kernel32.MEMORYSTATUSEX();
            double totalGB = 0;
            double freeGB = 0;

            if (Kernel32.GlobalMemoryStatusEx(memoryStatus))
            {
                freeGB = Math.Round((double)memoryStatus.ullAvailPhys / 1024 / 1024 / 1024, 2);
                totalGB = Math.Round((double)memoryStatus.ullTotalPhys / 1024 / 1024 / 1024, 2);
            }

            return (totalGB - freeGB, totalGB);
        }
        catch
        {
            return (0, 0);
        }
    }
}
