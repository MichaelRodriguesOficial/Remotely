using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Remotely.Shared.Services;

public interface IProcessInvoker
{
    string InvokeProcessOutput(string command, string arguments);
}

public class ProcessInvoker : IProcessInvoker
{
    private readonly ILogger<ProcessInvoker> _logger;

    public ProcessInvoker(ILogger<ProcessInvoker> logger)
    {
        _logger = logger;
    }

    public string InvokeProcessOutput(string command, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(command, arguments)
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "RunAs",
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return string.Empty;
            }

            // Lê a saída antes de esperar o fim: com saídas grandes o processo
            // trava esperando alguém esvaziar o buffer (deadlock).
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return output;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process.");
            return string.Empty;
        }
    }
}