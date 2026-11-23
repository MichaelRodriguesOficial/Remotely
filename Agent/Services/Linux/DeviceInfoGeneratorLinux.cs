using Microsoft.Extensions.Logging;
using Remotely.Agent.Interfaces;
using Remotely.Shared.Dtos;
using Remotely.Shared.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Remotely.Agent.Services.Linux;

public class DeviceInfoGeneratorLinux : DeviceInfoGeneratorBase, IDeviceInformationService
{
    private readonly IProcessInvoker _processInvoker;
    private readonly ICpuUtilizationSampler _cpuUtilSampler;

    public DeviceInfoGeneratorLinux(
        IProcessInvoker processInvoker, 
        ICpuUtilizationSampler cpuUtilSampler,
        ILogger<DeviceInfoGeneratorLinux> logger)
        : base(logger)
    {
        _processInvoker = processInvoker;
        _cpuUtilSampler = cpuUtilSampler;
    }

    public Task<DeviceClientDto> CreateDevice(string deviceId, string orgId)
    {
        var device = GetDeviceBase(deviceId, orgId);

        try
        {

            var (usedStorage, totalStorage) = GetSystemDriveInfo();
            var (usedMemory, totalMemory) = GetMemoryInGB();

            var userName = GetCurrentUser();
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
    /// "Ubuntu 24.04.1 LTS (6.8.0-41-generic)": nome da distribuição e versão do kernel.
    /// </summary>
    private static string? GetFriendlyOsDescription()
    {
        try
        {
            if (!File.Exists("/etc/os-release"))
            {
                return null;
            }

            var prettyName = File.ReadAllLines("/etc/os-release")
                .FirstOrDefault(x => x.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                ?.Split('=', 2)[1]
                .Trim('"', '\'', ' ');

            if (string.IsNullOrWhiteSpace(prettyName))
            {
                return null;
            }

            var kernel = File.Exists("/proc/sys/kernel/osrelease")
                ? File.ReadAllText("/proc/sys/kernel/osrelease").Trim()
                : string.Empty;

            return string.IsNullOrWhiteSpace(kernel) ? prettyName : $"{prettyName} ({kernel})";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Nome completo do usuário (campo GECOS do /etc/passwd), quando existir.
    /// </summary>
    private static string? GetDisplayName(string userName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userName) || !File.Exists("/etc/passwd"))
            {
                return null;
            }

            var entry = File.ReadLines("/etc/passwd")
                .Select(x => x.Split(':'))
                .FirstOrDefault(x => x.Length > 4 && x[0] == userName);

            var gecos = entry?[4].Split(',')[0].Trim();
            return string.IsNullOrWhiteSpace(gecos) ? null : gecos;
        }
        catch
        {
            return null;
        }
    }

    private string GetCurrentUser()
    {
        var users = _processInvoker.InvokeProcessOutput("users", "");
        return users?.Split()?.FirstOrDefault()?.Trim() ?? string.Empty;
    }

    public (double usedGB, double totalGB) GetMemoryInGB()
    {
        try
        {
            var results = _processInvoker.InvokeProcessOutput("cat", "/proc/meminfo");
            var resultsArr = results.Split("\n".ToCharArray());
            var freeKB = resultsArr
                        .FirstOrDefault(x => x.Trim().StartsWith("MemAvailable"))
                        ?.Trim()
                        ?.Split(" ".ToCharArray(), 2)
                        ?.Last() // 9168236 kB
                        ?.Trim()
                        ?.Split(' ')
                        ?.First() // 9168236
                        ?? "0";

            var totalKB = resultsArr
                        .FirstOrDefault(x => x.Trim().StartsWith("MemTotal"))
                        ?.Trim()
                        ?.Split(" ".ToCharArray(), 2)
                        ?.Last() // 16637468 kB
                        ?.Trim()
                        ?.Split(' ')
                        ?.First() // 16637468
                        ?? "0"; 

            var freeGB = Math.Round(double.Parse(freeKB) / 1024 / 1024, 2);
            var totalGB = Math.Round(double.Parse(totalKB) / 1024 / 1024, 2);

            return (totalGB - freeGB, totalGB);
        }
        catch
        {
            return (0, 0);
        }
    }
}
