using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using Remotely.Shared.Models.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static Remotely.Agent.Services.Diagnostics.DiagnosticHelpers;

namespace Remotely.Agent.Services.Diagnostics;

public interface IDiagnosticsRunner
{
    /// <summary>
    /// Executa os testes pedidos, chamando <paramref name="report"/> a cada mudança
    /// (para o painel mostrar o andamento).
    /// </summary>
    Task Run(string runId, IReadOnlyList<string> tests, string? serverHost, Func<DiagnosticReport, Task> report, CancellationToken cancellationToken);

    /// <summary>
    /// Se o computador reiniciou para o teste de memória, espera o resultado do Windows,
    /// refaz o teste Sistema e conclui a mesma execução.
    /// </summary>
    Task<bool> ResumeMemoryTest(string? serverHost, Func<DiagnosticReport, Task> report, SemaphoreSlim runLock, CancellationToken cancellationToken);

    /// <summary>Executa um reparo da lista fechada <see cref="DiagnosticRepairs"/>.</summary>
    Task RunRepair(string runId, string repairId, string? target, Func<DiagnosticReport, Task> report, CancellationToken cancellationToken);
}

public class DiagnosticsRunner : IDiagnosticsRunner
{
    private readonly ILogger<DiagnosticsRunner> _logger;

    public DiagnosticsRunner(ILogger<DiagnosticsRunner> logger)
    {
        _logger = logger;
    }

    public async Task Run(
        string runId,
        IReadOnlyList<string> tests,
        string? serverHost,
        Func<DiagnosticReport, Task> report,
        CancellationToken cancellationToken)
    {
        var diagnostic = new DiagnosticReport { RunId = runId, StartedAt = DateTimeOffset.Now };

        // Leves primeiro; o teste de memória (que reinicia) sempre por último.
        var ordered = DiagnosticTests.All
            .Where(x => tests.Contains(x.Id))
            .OrderBy(x => x.RequiresRestart)
            .ThenBy(x => x.Heavy)
            .ToList();

        foreach (var info in ordered)
        {
            diagnostic.Tests.Add(new DiagnosticTestResult { Id = info.Id, Name = info.Name, Status = DiagnosticStatus.Pending });
        }
        await report(diagnostic);

        foreach (var test in diagnostic.Tests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            test.Status = DiagnosticStatus.Running;
            test.StartedAt = DateTimeOffset.Now;
            await report(diagnostic);

            // Teste de memória (sempre o último): o computador reinicia e o agente completa
            // esta mesma execução com o resultado quando voltar (ResumeMemoryTest).
            if (test.Id == DiagnosticTests.MemoryTest && OperatingSystem.IsWindows())
            {
                var scheduledAt = DateTimeOffset.Now;
                MemoryTestMarker.Save(diagnostic, scheduledAt);
                if (WindowsDiagnostics.ScheduleMemoryTest(test))
                {
                    MemoryTestMarker.Save(diagnostic, scheduledAt);
                    await report(diagnostic);
                    return;
                }

                MemoryTestMarker.Delete();
                test.FinishedAt = DateTimeOffset.Now;
                await report(diagnostic);
                continue;
            }

            try
            {
                await RunTest(test, serverHost, cancellationToken);
                if (test.Status == DiagnosticStatus.Running)
                {
                    test.Status = test.Items.Count == 0 ? DiagnosticStatus.Skipped : Worst(test.Items);
                }
                test.Summary ??= DefaultSummary(test);
            }
            catch (OperationCanceledException)
            {
                test.Status = DiagnosticStatus.Skipped;
                test.Summary = "Cancelado.";
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Diagnostic test {test} failed.", test.Id);
                test.Status = DiagnosticStatus.Error;
                test.Summary = $"Falha ao executar o teste: {ex.Message}";
            }
            finally
            {
                test.FinishedAt = DateTimeOffset.Now;
            }

            await report(diagnostic);
        }

        diagnostic.Completed = true;
        diagnostic.FinishedAt = DateTimeOffset.Now;
        await report(diagnostic);
    }

    public async Task<bool> ResumeMemoryTest(string? serverHost, Func<DiagnosticReport, Task> report, SemaphoreSlim runLock, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || MemoryTestMarker.Load() is not { } pending)
        {
            return false;
        }

        var (diagnostic, scheduledAt) = pending;

        // Ainda não reiniciou (ex.: o agente só reconectou): espera o próximo início.
        var bootTime = DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (bootTime < scheduledAt && DateTimeOffset.Now - scheduledAt < TimeSpan.FromHours(12))
        {
            return false;
        }

        var memory = diagnostic.Tests.FirstOrDefault(x => x.Id == DiagnosticTests.MemoryTest);
        if (memory is null)
        {
            MemoryTestMarker.Delete();
            return false;
        }

        _logger.LogInformation("Resuming memory test of run {runId}.", diagnostic.RunId);

        // Mostra que o computador voltou enquanto o Windows registra o resultado.
        memory.Summary = "O computador voltou. Aguardando o resultado do Diagnóstico de Memória do Windows...";
        await report(diagnostic);

        // O Windows grava o resultado alguns minutos depois de iniciar (às vezes só após o logon).
        (bool HasErrors, DateTime Time)? result = null;
        var tooOld = DateTimeOffset.Now - scheduledAt > TimeSpan.FromHours(12);
        var deadline = DateTimeOffset.Now.AddMinutes(45);
        while (!tooOld && DateTimeOffset.Now < deadline)
        {
            result = WindowsDiagnostics.ReadMemoryResult(scheduledAt.LocalDateTime);
            if (result is not null)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }

        await runLock.WaitAsync(cancellationToken);
        try
        {
            memory.Items.Clear();
            if (result is { } found)
            {
                memory.Status = found.HasErrors ? DiagnosticStatus.Error : DiagnosticStatus.Ok;
                memory.Summary = found.HasErrors
                    ? "O Windows encontrou erros na memória RAM."
                    : "Nenhum erro encontrado na memória RAM.";
                memory.Items.Add(Item("Resultado", found.HasErrors ? "Encontrou erros" : "Sem erros",
                    memory.Status, found.HasErrors ? "Memória RAM com defeito: troque o módulo (teste um pente por vez para achar qual)." : null));
                memory.Items.Add(Item("Concluído em", found.Time.ToString("dd/MM/yyyy HH:mm")));
            }
            else
            {
                memory.Status = DiagnosticStatus.Warning;
                memory.Summary = "O computador voltou, mas o Windows não registrou o resultado.";
                memory.Items.Add(Item("Resultado", "Não registrado", DiagnosticStatus.Warning,
                    "O teste pode ter sido cancelado na tela (Esc) ou pulado no início do Windows. Rode de novo se precisar."));
            }
            memory.Items.Add(Item("Reiniciado em", (DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64)).ToString("dd/MM/yyyy HH:mm")));
            memory.FinishedAt = DateTimeOffset.Now;

            // O teste Sistema rodou antes do reinício: refaz agora (reinício pendente e último teste de memória).
            if (diagnostic.Tests.FirstOrDefault(x => x.Id == DiagnosticTests.System) is { } system)
            {
                system.Items.Clear();
                system.Summary = null;
                system.Status = DiagnosticStatus.Running;
                try
                {
                    await RunTest(system, serverHost, cancellationToken);
                    if (system.Status == DiagnosticStatus.Running)
                    {
                        system.Status = system.Items.Count == 0 ? DiagnosticStatus.Skipped : Worst(system.Items);
                    }
                    system.Summary = $"{DefaultSummary(system)} (refeito depois do reinício)";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "System test failed after memory test.");
                    system.Status = DiagnosticStatus.Error;
                    system.Summary = $"Falha ao executar o teste: {ex.Message}";
                }
                system.FinishedAt = DateTimeOffset.Now;
            }

            diagnostic.Completed = true;
            diagnostic.FinishedAt = DateTimeOffset.Now;
            await report(diagnostic);
            MemoryTestMarker.Delete();
            return true;
        }
        finally
        {
            runLock.Release();
        }
    }

    public async Task RunRepair(
        string runId,
        string repairId,
        string? target,
        Func<DiagnosticReport, Task> report,
        CancellationToken cancellationToken)
    {
        var diagnostic = new DiagnosticReport { RunId = runId, StartedAt = DateTimeOffset.Now };
        var repair = DiagnosticRepairs.Get(repairId);
        var test = new DiagnosticTestResult
        {
            Id = DiagnosticRepairs.ToTestId(repairId),
            Name = repair?.Name ?? repairId,
            Status = DiagnosticStatus.Running,
            StartedAt = DateTimeOffset.Now
        };
        diagnostic.Tests.Add(test);

        // O servidor já validou; valida de novo para não depender só dele.
        var normalized = repair is null ? null : DiagnosticRepairs.NormalizeTarget(repair, target);
        if (repair is null || normalized is null)
        {
            test.Status = DiagnosticStatus.Error;
            test.Summary = "Reparo ou alvo inválido.";
        }
        else
        {
            await report(diagnostic);
            _logger.LogInformation("Running repair {repair} ({target}). Run: {runId}", repair.Id, normalized, runId);
            try
            {
                await RepairRunner.Run(test, repair, normalized, () => report(diagnostic), cancellationToken);
                if (test.Status == DiagnosticStatus.Running)
                {
                    test.Status = test.Items.Count == 0 ? DiagnosticStatus.Skipped : Worst(test.Items);
                }
                test.Summary ??= test.Status switch
                {
                    DiagnosticStatus.Error => "O reparo não foi concluído. Veja os detalhes abaixo.",
                    DiagnosticStatus.Warning => "Reparo concluído, com pontos de atenção.",
                    DiagnosticStatus.Skipped => "Nada a fazer neste equipamento.",
                    _ => "Reparo concluído."
                };
            }
            catch (OperationCanceledException)
            {
                test.Status = DiagnosticStatus.Error;
                test.Summary = "Tempo limite esgotado ou agente encerrado durante o reparo.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Repair {repair} failed.", repair.Id);
                test.Status = DiagnosticStatus.Error;
                test.Summary = $"Falha ao executar o reparo: {ex.Message}";
            }
        }

        test.FinishedAt = DateTimeOffset.Now;
        diagnostic.Completed = true;
        diagnostic.FinishedAt = DateTimeOffset.Now;
        await report(diagnostic);
    }

    private async Task RunTest(DiagnosticTestResult test, string? serverHost, CancellationToken cancellationToken)
    {
        switch (test.Id)
        {
            case DiagnosticTests.Disks:
                if (OperatingSystem.IsWindows())
                {
                    WindowsDiagnostics.Disks(test);
                }
                else if (OperatingSystem.IsLinux())
                {
                    LinuxDiagnostics.Disks(test);
                }
                FreeSpace(test);
                break;

            case DiagnosticTests.Battery:
                var hasBattery = OperatingSystem.IsWindows() ? WindowsDiagnostics.Battery(test)
                    : OperatingSystem.IsLinux() && LinuxDiagnostics.Battery(test);
                if (!hasBattery)
                {
                    test.Status = DiagnosticStatus.Skipped;
                    test.Summary = "Sem bateria (computador de mesa ou servidor).";
                }
                break;

            case DiagnosticTests.Devices:
                if (OperatingSystem.IsWindows())
                {
                    WindowsDiagnostics.Devices(test);
                }
                else if (OperatingSystem.IsLinux())
                {
                    LinuxDiagnostics.Devices(test);
                }
                break;

            case DiagnosticTests.Network:
                await Network(test, serverHost, cancellationToken);
                break;

            case DiagnosticTests.System:
                Uptime(test);
                if (OperatingSystem.IsWindows())
                {
                    WindowsDiagnostics.SystemHealth(test);
                }
                else if (OperatingSystem.IsLinux())
                {
                    LinuxDiagnostics.SystemHealth(test);
                }
                break;

            case DiagnosticTests.DiskSpeed:
                await DiskSpeed(test, cancellationToken);
                break;

            case DiagnosticTests.CpuStress:
                await CpuStress(test, TimeSpan.FromSeconds(60), cancellationToken);
                break;

            case DiagnosticTests.MemoryTest:
                if (OperatingSystem.IsWindows())
                {
                    WindowsDiagnostics.ScheduleMemoryTest(test);
                }
                else
                {
                    test.Status = DiagnosticStatus.Skipped;
                    test.Summary = "Disponível só no Windows. No Linux, use o memtest86+ no menu de inicialização.";
                }
                break;
        }
    }

    private static string DefaultSummary(DiagnosticTestResult test)
    {
        var errors = test.Items.Count(x => x.Status == DiagnosticStatus.Error);
        var warnings = test.Items.Count(x => x.Status == DiagnosticStatus.Warning);
        return test.Status switch
        {
            DiagnosticStatus.Error => $"{errors} problema(s) encontrado(s){(warnings > 0 ? $" e {warnings} alerta(s)" : "")}.",
            DiagnosticStatus.Warning => $"{warnings} ponto(s) de atenção.",
            DiagnosticStatus.Skipped => "Sem dados para este equipamento.",
            _ => "Tudo certo."
        };
    }

    // -----------------------------------------------------------------
    // Espaço livre e tempo ligado (todos os sistemas)
    // -----------------------------------------------------------------

    private static void FreeSpace(DiagnosticTestResult test)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed || drive.TotalSize < 1024L * 1024 * 1024)
                {
                    continue;
                }
                var mount = drive.RootDirectory.FullName;
                if (!OperatingSystem.IsWindows() && (mount.StartsWith("/snap") || mount.StartsWith("/boot") || mount.StartsWith("/run")))
                {
                    continue;
                }

                var freePercent = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
                var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                var item = Item(
                    $"Espaço livre em {mount.TrimEnd('\\')}",
                    $"{freeGb:0.0} GB ({freePercent:0}%)".Replace(',', '.'),
                    freePercent < 5 ? DiagnosticStatus.Error : freePercent < 10 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                    freePercent < 10 ? "Pouco espaço deixa o sistema lento e impede atualizações." : null,
                    "Volumes");
                var isSystemVolume = OperatingSystem.IsWindows()
                    ? string.Equals(Path.GetPathRoot(Environment.SystemDirectory), mount, StringComparison.OrdinalIgnoreCase)
                    : mount == "/";
                if (freePercent < 10 && isSystemVolume)
                {
                    item.WithRepair(DiagnosticRepairs.Cleanup);
                }
                test.Items.Add(item);
            }
            catch
            {
                // Unidade indisponível.
            }
        }
    }

    private static void Uptime(DiagnosticTestResult test)
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var item = Item(
            "Tempo ligado",
            FormatDuration(uptime),
            uptime.TotalDays > 30 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
            uptime.TotalDays > 30 ? "Muito tempo sem reiniciar: atualizações e memória podem ficar pendentes." : null);
        if (uptime.TotalDays > 30)
        {
            item.WithRepair(DiagnosticRepairs.Reboot);
        }
        test.Items.Add(item);
    }

    // -----------------------------------------------------------------
    // Rede
    // -----------------------------------------------------------------

    private async Task Network(DiagnosticTestResult test, string? serverHost, CancellationToken cancellationToken)
    {
        var active = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                        x.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        x.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .ToList();

        if (active.Count == 0)
        {
            test.Status = DiagnosticStatus.Error;
            test.Summary = "Nenhuma rede com gateway ativa.";
            return;
        }

        var nic = active[0];
        var isWifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        var gateway = nic.GetIPProperties().GatewayAddresses
            .First(g => g.Address.AddressFamily == AddressFamily.InterNetwork).Address;

        test.Items.Add(Item("Interface", $"{nic.Name} ({(isWifi ? "Wi-Fi" : "cabo")})", group: "Conexão"));
        try
        {
            // Interfaces virtuais informam valores absurdos (ex.: 4 bilhões de Mbps).
            if (nic.Speed > 0 && nic.Speed <= 400_000_000_000)
            {
                var mbps = nic.Speed / 1_000_000;
                test.Items.Add(Item("Velocidade do link", mbps >= 1000 ? $"{mbps / 1000.0:0.#} Gbps".Replace(',', '.') : $"{mbps} Mbps",
                    !isWifi && mbps < 1000 ? DiagnosticStatus.Warning : DiagnosticStatus.Info,
                    !isWifi && mbps < 1000 ? "Cabo ou porta do switch negociando abaixo de 1 Gbps: verifique cabo e porta." : null,
                    "Conexão"));
            }
        }
        catch
        {
            // Velocidade indisponível.
        }

        if (isWifi)
        {
            var signal = OperatingSystem.IsWindows() ? WindowsDiagnostics.WifiSignal()
                : OperatingSystem.IsLinux() ? LinuxDiagnostics.WifiSignal() : null;
            if (int.TryParse(signal, out var percent))
            {
                test.Items.Add(Item("Sinal do Wi-Fi", $"{percent}%",
                    percent < 40 ? DiagnosticStatus.Error : percent < 60 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                    percent < 60 ? "Sinal fraco: aproxime do roteador ou use cabo." : null, "Conexão"));
            }
        }

        await PingTarget(test, "Gateway", gateway, isWifi ? 30 : 10, cancellationToken);
        await PingTarget(test, "Internet (1.1.1.1)", IPAddress.Parse("1.1.1.1"), 150, cancellationToken);
        await PingTarget(test, "Internet (8.8.8.8)", IPAddress.Parse("8.8.8.8"), 150, cancellationToken);

        // DNS.
        var dnsWatch = Stopwatch.StartNew();
        try
        {
            using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            dnsCts.CancelAfter(TimeSpan.FromSeconds(5));
            var addresses = await Dns.GetHostAddressesAsync("www.microsoft.com", dnsCts.Token);
            dnsWatch.Stop();
            test.Items.Add(Item("Resolução de nomes (DNS)", $"{dnsWatch.ElapsedMilliseconds} ms",
                dnsWatch.ElapsedMilliseconds > 500 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                dnsWatch.ElapsedMilliseconds > 500 ? "DNS lento." : null, "Testes"));
        }
        catch
        {
            test.Items.Add(Item("Resolução de nomes (DNS)", "Falhou", DiagnosticStatus.Error, "O computador não consegue resolver nomes. Verifique o DNS.", "Testes")
                .WithRepair(DiagnosticRepairs.NetworkFlush));
        }

        // Servidor do Remotely (TCP, porque ICMP costuma ser bloqueado).
        if (!string.IsNullOrWhiteSpace(serverHost) && Uri.TryCreate(serverHost, UriKind.Absolute, out var serverUri))
        {
            var times = new List<long>();
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    using var client = new TcpClient();
                    var watch = Stopwatch.StartNew();
                    using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                    await client.ConnectAsync(serverUri.Host, serverUri.Port, connectCts.Token);
                    times.Add(watch.ElapsedMilliseconds);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Tempo esgotado.
                }
                catch (SocketException)
                {
                    // Sem conexão.
                }
            }

            test.Items.Add(times.Count == 0
                ? Item("Servidor do Remotely", "Sem conexão", DiagnosticStatus.Error, group: "Testes")
                : Item("Servidor do Remotely", $"{times.Average():0} ms ({serverUri.Host})",
                    times.Average() > 300 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok, group: "Testes"));

            // Se o servidor responde mas nenhum ping à internet volta, o ICMP está bloqueado
            // (firewall da empresa), não é falta de conexão.
            var internetPings = test.Items.Where(x => x.Label.StartsWith("Internet", StringComparison.Ordinal)).ToList();
            if (times.Count > 0 && internetPings.Count > 0 && internetPings.All(x => x.Status == DiagnosticStatus.Error))
            {
                foreach (var item in internetPings)
                {
                    item.Status = DiagnosticStatus.Info;
                    item.Hint = "O firewall da rede bloqueia ping; a conexão com o servidor funciona.";
                }
            }
        }
    }

    private static async Task PingTarget(DiagnosticTestResult test, string label, IPAddress address, int warnLatencyMs, CancellationToken cancellationToken)
    {
        const int count = 10;
        var times = new List<long>();
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(address, 1000);
                if (reply.Status == IPStatus.Success)
                {
                    times.Add(reply.RoundtripTime);
                }
            }
            catch (PingException)
            {
                // Conta como perdido.
            }
            await Task.Delay(100, cancellationToken);
        }

        var loss = 100.0 * (count - times.Count) / count;
        if (times.Count == 0)
        {
            test.Items.Add(Item(label, $"Sem resposta ({address})", DiagnosticStatus.Error,
                "Pode ser bloqueio de ping no firewall ou falta de conexão.", "Testes"));
            return;
        }

        var average = times.Average();
        var status = loss >= 20 ? DiagnosticStatus.Error
            : loss > 0 || average > warnLatencyMs ? DiagnosticStatus.Warning
            : DiagnosticStatus.Ok;

        test.Items.Add(Item(
            label,
            $"{average:0} ms em média (máx. {times.Max()} ms), perda de {loss:0}%",
            status,
            loss > 0 ? "Pacotes perdidos: cabo, Wi-Fi ou equipamento de rede com problema." :
                average > warnLatencyMs ? "Latência alta." : null,
            "Testes"));
    }

    // -----------------------------------------------------------------
    // Velocidade do disco
    // -----------------------------------------------------------------

    private async Task DiskSpeed(DiagnosticTestResult test, CancellationToken cancellationToken)
    {
        var directory = OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Windows", "Temp")
            : "/var/tmp";
        if (!Directory.Exists(directory))
        {
            directory = Path.GetTempPath();
        }

        var drive = new DriveInfo(Path.GetPathRoot(directory) ?? directory);
        const long maxSize = 1024L * 1024 * 1024;
        var size = Math.Min(maxSize, drive.AvailableFreeSpace / 10);
        const int blockSize = 4 * 1024 * 1024;
        size -= size % blockSize;
        if (size < 64L * 1024 * 1024)
        {
            test.Status = DiagnosticStatus.Skipped;
            test.Summary = "Pouco espaço livre para o teste.";
            return;
        }

        var path = Path.Combine(directory, $"remotely-disk-test-{Guid.NewGuid():N}.tmp");

        // Buffer alinhado em 4 KB (exigência da escrita sem cache do Windows).
        var raw = GC.AllocateUninitializedArray<byte>(blockSize + 4096, pinned: true);
        var address = Marshal.UnsafeAddrOfPinnedArrayElement(raw, 0).ToInt64();
        var offset = (int)((4096 - address % 4096) % 4096);
        var buffer = raw.AsMemory(offset, blockSize);
        Random.Shared.NextBytes(buffer.Span);

        // 0x20000000 = FILE_FLAG_NO_BUFFERING: mede o disco, não a memória.
        var options = OperatingSystem.IsWindows()
            ? FileOptions.WriteThrough | (FileOptions)0x20000000
            : FileOptions.WriteThrough;

        try
        {
            double writeSeconds;
            using (var handle = File.OpenHandle(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, options, size))
            {
                var watch = Stopwatch.StartNew();
                for (long position = 0; position < size; position += blockSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RandomAccess.WriteAsync(handle, buffer, position, cancellationToken);
                }
                watch.Stop();
                writeSeconds = watch.Elapsed.TotalSeconds;
            }

            var cacheDropped = OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && DropFileCache(path));

            double readSeconds;
            using (var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None,
                       OperatingSystem.IsWindows() ? (FileOptions)0x20000000 : FileOptions.SequentialScan))
            {
                var watch = Stopwatch.StartNew();
                for (long position = 0; position < size; position += blockSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RandomAccess.ReadAsync(handle, buffer, position, cancellationToken);
                }
                watch.Stop();
                readSeconds = watch.Elapsed.TotalSeconds;
            }

            var megabytes = size / 1024.0 / 1024;
            var write = megabytes / Math.Max(writeSeconds, 0.001);
            var read = megabytes / Math.Max(readSeconds, 0.001);

            test.Items.Add(Item("Arquivo de teste", $"{megabytes:0} MB em {directory}"));
            test.Items.Add(Item("Escrita sequencial", $"{write:0} MB/s",
                write < 20 ? DiagnosticStatus.Error : write < 60 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                write < 60 ? "Disco lento. SSDs costumam passar de 300 MB/s e HDs de 80 MB/s." : null));
            test.Items.Add(Item("Leitura sequencial", $"{read:0} MB/s{(cacheDropped ? "" : " (pode incluir cache)")}",
                read < 30 ? DiagnosticStatus.Error : read < 80 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                read < 80 ? "Disco lento. SSDs costumam passar de 400 MB/s e HDs de 100 MB/s." : null));
            test.Summary = $"Escrita {write:0} MB/s, leitura {read:0} MB/s.";
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to delete disk test file {path}.", path);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PosixFadvise(int fd, long offset, long len, int advice);

    private bool DropFileCache(string path)
    {
        try
        {
            IntPtr library = IntPtr.Zero;
            foreach (var name in new[] { "libc.so.6", "libc.so", "libc" })
            {
                if (NativeLibrary.TryLoad(name, out library))
                {
                    break;
                }
            }
            if (library == IntPtr.Zero || !NativeLibrary.TryGetExport(library, "posix_fadvise", out var export))
            {
                return false;
            }

            var fadvise = Marshal.GetDelegateForFunctionPointer<PosixFadvise>(export);
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read);
            var added = false;
            try
            {
                handle.DangerousAddRef(ref added);
                // 4 = POSIX_FADV_DONTNEED: tira o arquivo do cache para a leitura ir ao disco.
                return fadvise((int)handle.DangerousGetHandle(), 0, 0, 4) == 0;
            }
            finally
            {
                if (added)
                {
                    handle.DangerousRelease();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "posix_fadvise unavailable.");
            return false;
        }
    }

    // -----------------------------------------------------------------
    // Estresse de CPU
    // -----------------------------------------------------------------

    private async Task CpuStress(DiagnosticTestResult test, TimeSpan duration, CancellationToken cancellationToken)
    {
        (double? Mhz, double? BaseMhz) ReadFrequency() =>
            OperatingSystem.IsWindows() ? WindowsDiagnostics.CpuFrequency()
            : OperatingSystem.IsLinux() ? LinuxDiagnostics.CpuFrequency()
            : (null, null);

        double? ReadTemperature() =>
            OperatingSystem.IsWindows() ? WindowsDiagnostics.CpuTemperature()
            : OperatingSystem.IsLinux() ? LinuxDiagnostics.CpuTemperature()
            : null;

        var (idleMhz, baseMhz) = ReadFrequency();
        var idleTemperature = ReadTemperature();

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var counters = new long[Environment.ProcessorCount];
        var threads = new List<Thread>();
        for (var i = 0; i < counters.Length; i++)
        {
            var index = i;
            var thread = new Thread(() =>
            {
                double value = index + 1.5;
                long iterations = 0;
                while (!stop.IsCancellationRequested)
                {
                    for (var j = 0; j < 10_000; j++)
                    {
                        value = Math.Sqrt(value * 1.000001 + j) + Math.Sin(value);
                    }
                    iterations++;
                }
                counters[index] = iterations + (value > double.MaxValue ? 1 : 0);
            })
            {
                IsBackground = true,
                Name = $"Remotely CPU stress {i}"
            };
            threads.Add(thread);
        }

        var watch = Stopwatch.StartNew();
        threads.ForEach(x => x.Start());

        var samples = new List<double>();
        var temperatures = new List<double>();
        try
        {
            // Os primeiros segundos de carga podem usar turbo; amostras a cada 3 s.
            while (watch.Elapsed < duration)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                if (ReadFrequency().Mhz is { } mhz)
                {
                    samples.Add(mhz);
                }
                if (ReadTemperature() is { } temperature)
                {
                    temperatures.Add(temperature);
                }
            }
        }
        finally
        {
            stop.Cancel();
            threads.ForEach(x => x.Join(TimeSpan.FromSeconds(5)));
            watch.Stop();
        }

        var score = counters.Sum() / Math.Max(watch.Elapsed.TotalSeconds, 1);
        test.Items.Add(Item("Duração", $"{watch.Elapsed.TotalSeconds:0} s com {counters.Length} threads a 100%"));
        test.Items.Add(Item("Pontuação", $"{score:0} blocos/s (compare com execuções anteriores)"));

        if (idleMhz is { } idle)
        {
            test.Items.Add(Item("Frequência em repouso", $"{idle:0} MHz"));
        }

        if (samples.Count > 0)
        {
            var average = samples.Average();
            var minimum = samples.Min();
            var throttled = baseMhz is > 0 && minimum < baseMhz.Value * 0.6;
            test.Items.Add(Item("Frequência sob carga", $"{average:0} MHz em média, mínima {minimum:0} MHz{(baseMhz is > 0 ? $" (base {baseMhz:0} MHz)" : "")}",
                throttled ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                throttled ? "A frequência caiu muito sob carga (throttling): superaquecimento, poeira, pasta térmica ou plano de energia." : null));
        }

        if (temperatures.Count > 0)
        {
            var max = temperatures.Max();
            test.Items.Add(Item("Temperatura máxima", $"{max:0} °C{(idleTemperature is { } t ? $" (em repouso {t:0} °C)" : "")}",
                max >= 95 ? DiagnosticStatus.Error : max >= 90 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                max >= 90 ? "Temperatura alta: limpe o cooler e verifique a pasta térmica." : null));
        }
        else
        {
            test.Items.Add(Item("Temperatura", "Não informada por este equipamento"));
        }
    }
}
