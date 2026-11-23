using Remotely.Shared.Models.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Remotely.Agent.Services.Diagnostics.DiagnosticHelpers;

namespace Remotely.Agent.Services.Diagnostics;

/// <summary>
/// Executa os reparos da lista fechada <see cref="DiagnosticRepairs"/>. Cada passo vira um item
/// do relatório, com o resultado e (quando falha) o final da saída do comando.
/// Os alvos são validados de novo aqui e passados como argumentos separados, nunca por um shell.
/// </summary>
internal static class RepairRunner
{
    public static async Task Run(DiagnosticTestResult test, DiagnosticRepairs.RepairInfo repair, string target, Func<Task> progress, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await RunWindows(test, repair, target, progress, cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await RunLinux(test, repair, target, progress, cancellationToken);
        }
        else
        {
            Skip(test, "Reparo não disponível neste sistema.");
        }
    }

    // -----------------------------------------------------------------
    // Windows
    // -----------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static async Task RunWindows(DiagnosticTestResult test, DiagnosticRepairs.RepairInfo repair, string target, Func<Task> progress, CancellationToken ct)
    {
        switch (repair.Id)
        {
            case DiagnosticRepairs.Reboot:
            {
                var seconds = DiagnosticRepairs.RebootDelaySeconds(target, DateTime.Now);
                var when = DateTime.Now.AddSeconds(seconds);
                var message = target.Contains(':')
                    ? $"O suporte de TI vai reiniciar este computador às {target}. Salve seu trabalho antes disso."
                    : $"O suporte de TI vai reiniciar este computador em {FormatMinutes(seconds)}. Salve seu trabalho.";
                var (exit, output) = await Tool(System32("shutdown.exe"), ["/r", "/t", seconds.ToString(CultureInfo.InvariantCulture), "/c", message, "/d", "p:4:1"], 30, ct);
                if (exit == 0)
                {
                    Step(test, "Reinício agendado", $"{when:dd/MM/yyyy HH:mm} ({FormatMinutes(seconds)})", DiagnosticStatus.Ok);
                    Step(test, "Aviso ao usuário", "Mensagem exibida na tela");
                    test.Summary = $"O computador reinicia em {when:dd/MM HH:mm}.";
                }
                else if (exit == 1190)
                {
                    Step(test, "Reinício", "Já existe um reinício agendado", DiagnosticStatus.Warning,
                        "Cancele o reinício atual antes de agendar outro.").WithRepair(DiagnosticRepairs.CancelReboot);
                }
                else
                {
                    Failed(test, "Reinício", exit, output);
                }
                break;
            }

            case DiagnosticRepairs.CancelReboot:
            {
                var (exit, output) = await Tool(System32("shutdown.exe"), ["/a"], 30, ct);
                if (exit == 0)
                {
                    Step(test, "Reinício", "Cancelado", DiagnosticStatus.Ok);
                }
                else if (exit == 1116)
                {
                    Step(test, "Reinício", "Não havia reinício agendado", DiagnosticStatus.Ok);
                }
                else
                {
                    Failed(test, "Cancelar reinício", exit, output);
                }
                break;
            }

            case DiagnosticRepairs.DiskScan:
            {
                Step(test, "Unidade", target);
                await progress();
                var (exit, output) = await Tool(System32("chkdsk.exe"), [target, "/scan"], 45 * 60, ct);
                switch (exit)
                {
                    case 0:
                        Step(test, "Resultado", "Nenhum problema encontrado", DiagnosticStatus.Ok);
                        break;
                    case 1:
                        Step(test, "Resultado", "Problemas encontrados e corrigidos", DiagnosticStatus.Ok, Tail(output));
                        break;
                    case 2:
                        Step(test, "Resultado", "Limpeza do sistema de arquivos feita", DiagnosticStatus.Ok);
                        break;
                    case 3:
                        Step(test, "Resultado", "Precisa de reparo com a unidade fora de uso", DiagnosticStatus.Warning,
                            "Agende o reparo completo para o próximo reinício.")
                            .WithRepair(DiagnosticRepairs.DiskRepairOnBoot, target);
                        break;
                    default:
                        Failed(test, "chkdsk", exit, output);
                        break;
                }
                break;
            }

            case DiagnosticRepairs.DiskRepairOnBoot:
            {
                var (exit, output) = await Tool(System32("fsutil.exe"), ["dirty", "set", target], 60, ct);
                if (exit == 0)
                {
                    Step(test, "Reparo completo", $"Agendado para o próximo início ({target})", DiagnosticStatus.Ok);
                    RebootNeeded(test, "A verificação roda antes do Windows abrir e pode levar de 10 minutos a mais de 1 hora.");
                }
                else
                {
                    Failed(test, "Agendar reparo", exit, output);
                }
                break;
            }

            case DiagnosticRepairs.Cleanup:
            {
                var systemDrive = SystemDrive();
                var before = FreeBytes(systemDrive);

                var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var tempDirs = new List<string> { Path.Combine(windir, "Temp") };
                var usersRoot = Path.Combine(systemDrive + "\\", "Users");
                if (Directory.Exists(usersRoot))
                {
                    tempDirs.AddRange(SafeDirectories(usersRoot)
                        .Select(x => Path.Combine(x, "AppData", "Local", "Temp"))
                        .Where(Directory.Exists));
                }
                var (tempFiles, tempBytes) = DeleteContents(tempDirs, TimeSpan.FromDays(2), ct);
                Step(test, "Arquivos temporários", $"{tempFiles} arquivo(s), {FormatBytes(tempBytes)}", DiagnosticStatus.Ok);
                await progress();

                var recycleDirs = DriveInfo.GetDrives()
                    .Where(x => x.DriveType == DriveType.Fixed && x.IsReady)
                    .Select(x => Path.Combine(x.RootDirectory.FullName, "$Recycle.Bin"))
                    .Where(Directory.Exists)
                    .SelectMany(SafeDirectories)
                    .ToList();
                var (binFiles, binBytes) = DeleteContents(recycleDirs, TimeSpan.Zero, ct, keep: "desktop.ini");
                Step(test, "Lixeira", $"{binFiles} arquivo(s), {FormatBytes(binBytes)}", DiagnosticStatus.Ok);
                await progress();

                await Tool(System32("net.exe"), ["stop", "wuauserv"], 120, ct);
                var (wuFiles, wuBytes) = DeleteContents([Path.Combine(windir, "SoftwareDistribution", "Download")], TimeSpan.Zero, ct);
                await Tool(System32("net.exe"), ["start", "wuauserv"], 120, ct);
                Step(test, "Cache do Windows Update", $"{wuFiles} arquivo(s), {FormatBytes(wuBytes)}", DiagnosticStatus.Ok);
                await progress();

                var (dismExit, dismOutput) = await Tool(System32("dism.exe"), ["/Online", "/Cleanup-Image", "/StartComponentCleanup"], 45 * 60, ct);
                if (dismExit == 0)
                {
                    Step(test, "Componentes do Windows substituídos", "Removidos", DiagnosticStatus.Ok);
                }
                else
                {
                    Step(test, "Componentes do Windows substituídos", $"Não concluído (código {dismExit})", DiagnosticStatus.Info, Tail(dismOutput));
                }

                var freed = Math.Max(0, FreeBytes(systemDrive) - before);
                Step(test, $"Espaço liberado em {systemDrive}", $"{FormatBytes(freed)} (livre agora: {FormatBytes(FreeBytes(systemDrive))})", DiagnosticStatus.Ok);
                test.Summary = $"{FormatBytes(freed)} liberados.";
                break;
            }

            case DiagnosticRepairs.SystemFiles:
            {
                Step(test, "Restaurar imagem do Windows (DISM)", "Executando...", DiagnosticStatus.Running);
                await progress();
                var (dismExit, dismOutput) = await Tool(System32("dism.exe"), ["/Online", "/Cleanup-Image", "/RestoreHealth"], 60 * 60, ct);
                test.Items.RemoveAt(test.Items.Count - 1);
                if (dismExit == 0)
                {
                    Step(test, "Imagem do Windows (DISM)", "Íntegra ou restaurada", DiagnosticStatus.Ok);
                }
                else
                {
                    Step(test, "Imagem do Windows (DISM)", $"Falhou (código {dismExit})", DiagnosticStatus.Warning,
                        Tail(dismOutput) ?? "Sem acesso ao Windows Update ou à origem dos arquivos.");
                }

                Step(test, "Verificar arquivos de sistema (sfc)", "Executando...", DiagnosticStatus.Running);
                await progress();
                var (_, sfcOutput) = await Tool(System32("sfc.exe"), ["/scannow"], 60 * 60, ct, Encoding.Unicode);
                test.Items.RemoveAt(test.Items.Count - 1);
                var normalized = Normalize(sfcOutput);
                if (normalized.Contains("did not find any integrity violations") || normalized.Contains("nao encontrou nenhuma violacao"))
                {
                    Step(test, "Arquivos de sistema (sfc)", "Nenhum arquivo corrompido", DiagnosticStatus.Ok);
                }
                else if (normalized.Contains("successfully repaired") || normalized.Contains("reparou-os com exito") || normalized.Contains("reparou com exito"))
                {
                    Step(test, "Arquivos de sistema (sfc)", "Arquivos corrompidos encontrados e reparados", DiagnosticStatus.Ok);
                    RebootNeeded(test, "Reinicie para concluir o reparo.");
                }
                else if (normalized.Contains("unable to fix") || normalized.Contains("nao pode corrigir") || normalized.Contains("nao conseguiu corrigir"))
                {
                    Step(test, "Arquivos de sistema (sfc)", "Há arquivos corrompidos que não foram reparados", DiagnosticStatus.Error,
                        "Rode de novo depois de reiniciar. Se continuar, a reinstalação/reparo do Windows pode ser necessária. Detalhes em C:\\Windows\\Logs\\CBS\\CBS.log.");
                }
                else
                {
                    Step(test, "Arquivos de sistema (sfc)", "Não foi possível concluir", DiagnosticStatus.Warning,
                        Tail(sfcOutput) ?? "Reinicie e tente de novo.");
                }
                break;
            }

            case DiagnosticRepairs.NetworkFlush:
            {
                var (flushExit, flushOutput) = await Tool(System32("ipconfig.exe"), ["/flushdns"], 30, ct);
                if (flushExit == 0)
                {
                    Step(test, "Cache de DNS", "Limpo", DiagnosticStatus.Ok);
                }
                else
                {
                    Failed(test, "Cache de DNS", flushExit, flushOutput);
                }

                var (renewExit, renewOutput) = await Tool(System32("ipconfig.exe"), ["/renew"], 120, ct);
                Step(test, "Renovar IP (DHCP)",
                    renewExit == 0 ? "Renovado" : "Não renovado",
                    renewExit == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
                    renewExit == 0 ? null : Tail(renewOutput) ?? "A placa pode estar com IP fixo ou o servidor DHCP não respondeu.");

                await Tool(System32("ipconfig.exe"), ["/registerdns"], 60, ct);
                Step(test, "Registrar no DNS", "Solicitado");
                break;
            }

            case DiagnosticRepairs.NetworkReset:
            {
                var (winsockExit, winsockOutput) = await Tool(System32("netsh.exe"), ["winsock", "reset"], 60, ct);
                if (winsockExit == 0) Step(test, "Winsock", "Redefinido", DiagnosticStatus.Ok);
                else Failed(test, "Winsock", winsockExit, winsockOutput);

                var (ipExit, ipOutput) = await Tool(System32("netsh.exe"), ["int", "ip", "reset"], 60, ct);
                // Devolve 1 quando alguma chave já estava no padrão; não é erro.
                if (ipExit is 0 or 1) Step(test, "TCP/IP", "Redefinido", DiagnosticStatus.Ok);
                else Failed(test, "TCP/IP", ipExit, ipOutput);

                RebootNeeded(test, "As configurações de rede só valem depois de reiniciar.");
                break;
            }

            case DiagnosticRepairs.WindowsUpdateReset:
            {
                string[] services = ["wuauserv", "bits", "cryptsvc", "usosvc"];
                foreach (var service in services)
                {
                    await Tool(System32("net.exe"), ["stop", service], 120, ct);
                }
                Step(test, "Serviços de atualização", "Parados");
                await progress();

                var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                Step(test, "Cache de atualizações (SoftwareDistribution)",
                    RenameToBackup(Path.Combine(windir, "SoftwareDistribution")) ? "Recriado" : "Não foi possível renomear (em uso)",
                    DiagnosticStatus.Ok);
                Step(test, "Catálogo de assinaturas (catroot2)",
                    RenameToBackup(Path.Combine(windir, "System32", "catroot2")) ? "Recriado" : "Não foi possível renomear (em uso)",
                    DiagnosticStatus.Ok);

                var failedStart = new List<string>();
                foreach (var service in services.Reverse())
                {
                    var (exit, _) = await Tool(System32("net.exe"), ["start", service], 120, ct);
                    // 2 = já estava rodando. usosvc pode iniciar sob demanda.
                    if (exit is not (0 or 2) && service != "usosvc")
                    {
                        failedStart.Add(service);
                    }
                }
                Step(test, "Serviços de atualização",
                    failedStart.Count == 0 ? "Iniciados" : $"Não iniciou: {string.Join(", ", failedStart)}",
                    failedStart.Count == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning);

                var uso = System32("UsoClient.exe");
                if (File.Exists(uso))
                {
                    await Tool(uso, ["StartScan"], 60, ct);
                    Step(test, "Nova busca por atualizações", "Solicitada", DiagnosticStatus.Ok,
                        "O download e a instalação seguem em segundo plano, conforme as políticas do Windows Update.");
                }
                break;
            }

            case DiagnosticRepairs.FirewallEnable:
            {
                var (exit, output) = await Tool(System32("netsh.exe"), ["advfirewall", "set", "allprofiles", "state", "on"], 60, ct);
                if (exit != 0)
                {
                    Failed(test, "Firewall", exit, output);
                    break;
                }

                var disabled = Wmi("SELECT Name, Enabled FROM MSFT_NetFirewallProfile", @"root\StandardCimv2")
                    .Where(x => ToLong(Get(x, "Enabled")) is 0)
                    .Select(x => ToText(Get(x, "Name")))
                    .ToList();
                Step(test, "Firewall do Windows",
                    disabled.Count == 0 ? "Ativo em todos os perfis" : $"Continua desligado: {string.Join(", ", disabled)}",
                    disabled.Count == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
                    disabled.Count == 0 ? null : "Uma política de grupo (GPO) ou outro firewall pode estar desligando o do Windows.");
                break;
            }

            case DiagnosticRepairs.DefenderUpdate:
            {
                var mpcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
                if (!File.Exists(mpcmd))
                {
                    Skip(test, "Microsoft Defender não encontrado neste computador.");
                    break;
                }

                var (exit, output) = await Tool(mpcmd, ["-SignatureUpdate"], 15 * 60, ct);
                if (exit == 0)
                {
                    Step(test, "Definições do antivírus", "Atualizadas", DiagnosticStatus.Ok);
                }
                else
                {
                    Failed(test, "Atualizar definições", exit, output);
                }
                break;
            }

            case DiagnosticRepairs.TimeSync:
            {
                await Tool(System32("sc.exe"), ["config", "w32time", "start=", "auto"], 30, ct);
                await Tool(System32("net.exe"), ["start", "w32time"], 60, ct);
                var (exit, output) = await Tool(System32("w32tm.exe"), ["/resync", "/force"], 60, ct);
                if (exit == 0)
                {
                    Step(test, "Sincronização", "Feita", DiagnosticStatus.Ok);
                }
                else
                {
                    Step(test, "Sincronização", "Não sincronizou", DiagnosticStatus.Warning,
                        Tail(output) ?? "Verifique se o servidor de horário (domínio ou time.windows.com) está acessível.");
                }
                Step(test, "Hora do computador", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
                break;
            }

            case DiagnosticRepairs.DeviceRestart:
            {
                var name = DeviceName(target) ?? target;
                Step(test, "Dispositivo", name);
                var (exit, output) = await Tool(System32("pnputil.exe"), ["/restart-device", target], 120, ct);
                if (exit != 0)
                {
                    // Windows antigo: sem /restart-device.
                    await Tool(System32("pnputil.exe"), ["/disable-device", target], 120, ct);
                    (exit, output) = await Tool(System32("pnputil.exe"), ["/enable-device", target], 120, ct);
                }

                if (exit is not (0 or 3010))
                {
                    Failed(test, "Reiniciar o dispositivo", exit, output);
                    break;
                }

                await Task.Delay(3_000, ct);
                var code = DeviceErrorCode(target);
                if (code is 0 or null)
                {
                    Step(test, "Situação", "Funcionando", DiagnosticStatus.Ok);
                }
                else
                {
                    Step(test, "Situação", $"Continua com erro (código {code})", DiagnosticStatus.Warning,
                        "Reinstale ou atualize o driver. Se persistir, pode ser defeito no hardware.");
                }
                if (exit == 3010)
                {
                    RebootNeeded(test, "O driver pede reinício para concluir.");
                }
                break;
            }

            case DiagnosticRepairs.DeviceRescan:
            {
                var (exit, output) = await Tool(System32("pnputil.exe"), ["/scan-devices"], 120, ct);
                if (exit == 0)
                {
                    await Task.Delay(5_000, ct);
                    var problems = Wmi("SELECT Name FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 22 AND ConfigManagerErrorCode <> 24 AND ConfigManagerErrorCode <> 45").Count;
                    Step(test, "Procura de hardware", "Concluída", DiagnosticStatus.Ok);
                    Step(test, "Dispositivos com erro agora", problems.ToString(CultureInfo.InvariantCulture),
                        problems == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
                        problems == 0 ? null : "Veja o teste \"Dispositivos com erro\" para saber quais.");
                }
                else
                {
                    Failed(test, "Procurar hardware", exit, output);
                }
                break;
            }

            default:
                Skip(test, "Reparo não disponível no Windows.");
                break;
        }
    }

    // -----------------------------------------------------------------
    // Linux
    // -----------------------------------------------------------------

    private static async Task RunLinux(DiagnosticTestResult test, DiagnosticRepairs.RepairInfo repair, string target, Func<Task> progress, CancellationToken ct)
    {
        switch (repair.Id)
        {
            case DiagnosticRepairs.Reboot:
            {
                var seconds = DiagnosticRepairs.RebootDelaySeconds(target, DateTime.Now);
                var minutes = Math.Max(1, (int)Math.Ceiling(seconds / 60.0));
                var when = DateTime.Now.AddMinutes(minutes);
                var (exit, output) = await Tool("shutdown", ["-r", $"+{minutes}", "O suporte de TI vai reiniciar este computador. Salve seu trabalho."], 30, ct);
                if (exit == 0)
                {
                    Step(test, "Reinício agendado", $"{when:dd/MM/yyyy HH:mm} ({FormatMinutes(minutes * 60)})", DiagnosticStatus.Ok);
                    test.Summary = $"O computador reinicia em {when:dd/MM HH:mm}.";
                }
                else
                {
                    Failed(test, "Reinício", exit, output);
                }
                break;
            }

            case DiagnosticRepairs.CancelReboot:
            {
                var (exit, output) = await Tool("shutdown", ["-c"], 30, ct);
                if (exit == 0) Step(test, "Reinício", "Cancelado (se havia)", DiagnosticStatus.Ok);
                else Failed(test, "Cancelar reinício", exit, output);
                break;
            }

            case DiagnosticRepairs.Cleanup:
            {
                var before = FreeBytes("/");
                if (File.Exists("/usr/bin/apt-get"))
                {
                    var (exit, _) = await Tool("apt-get", ["clean"], 300, ct);
                    Step(test, "Cache do apt", exit == 0 ? "Limpo" : $"Falhou (código {exit})", exit == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Info);
                }
                else if (File.Exists("/usr/bin/dnf"))
                {
                    var (exit, _) = await Tool("dnf", ["clean", "all"], 300, ct);
                    Step(test, "Cache do dnf", exit == 0 ? "Limpo" : $"Falhou (código {exit})", exit == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Info);
                }
                await progress();

                if (File.Exists("/usr/bin/journalctl") || File.Exists("/bin/journalctl"))
                {
                    var (exit, _) = await Tool("journalctl", ["--vacuum-time=14d"], 300, ct);
                    Step(test, "Logs do sistema (journal)", exit == 0 ? "Mantidos só os últimos 14 dias" : $"Falhou (código {exit})",
                        exit == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Info);
                }

                var (tmpFiles, tmpBytes) = DeleteContents(["/var/tmp"], TimeSpan.FromDays(30), ct);
                Step(test, "Temporários antigos (/var/tmp)", $"{tmpFiles} arquivo(s), {FormatBytes(tmpBytes)}", DiagnosticStatus.Ok);

                var freed = Math.Max(0, FreeBytes("/") - before);
                Step(test, "Espaço liberado em /", FormatBytes(freed), DiagnosticStatus.Ok);
                test.Summary = $"{FormatBytes(freed)} liberados.";
                break;
            }

            case DiagnosticRepairs.NetworkFlush:
            {
                var (exit, output) = await Tool("resolvectl", ["flush-caches"], 30, ct);
                if (exit != 0)
                {
                    (exit, output) = await Tool("systemd-resolve", ["--flush-caches"], 30, ct);
                }
                if (exit == 0) Step(test, "Cache de DNS", "Limpo", DiagnosticStatus.Ok);
                else Step(test, "Cache de DNS", "Sem cache local (systemd-resolved não está em uso)", DiagnosticStatus.Info, Tail(output));
                break;
            }

            case DiagnosticRepairs.TimeSync:
            {
                await Tool("timedatectl", ["set-ntp", "true"], 30, ct);
                var (exit, _) = await Tool("chronyc", ["-a", "makestep"], 30, ct);
                if (exit != 0)
                {
                    (exit, _) = await Tool("systemctl", ["restart", "systemd-timesyncd"], 60, ct);
                }
                Step(test, "Sincronização", exit == 0 ? "Feita" : "Não foi possível forçar",
                    exit == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning);
                Step(test, "Hora do computador", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
                break;
            }

            default:
                Skip(test, "Reparo disponível só no Windows.");
                break;
        }
    }

    // -----------------------------------------------------------------
    // Auxiliares
    // -----------------------------------------------------------------

    private static DiagnosticItem Step(DiagnosticTestResult test, string label, string value, DiagnosticStatus status = DiagnosticStatus.Info, string? hint = null)
    {
        var item = Item(label, value, status, hint);
        test.Items.Add(item);
        return item;
    }

    private static void Failed(DiagnosticTestResult test, string label, int exitCode, string output) =>
        Step(test, label, exitCode == -1 ? "Não respondeu no tempo limite" : $"Falhou (código {exitCode})",
            DiagnosticStatus.Error, Tail(output));

    private static void Skip(DiagnosticTestResult test, string reason)
    {
        test.Status = DiagnosticStatus.Skipped;
        test.Summary = reason;
    }

    private static void RebootNeeded(DiagnosticTestResult test, string hint) =>
        Step(test, "Reinício necessário", "Sim", DiagnosticStatus.Warning, hint).WithRepair(DiagnosticRepairs.Reboot);

    private static string FormatMinutes(int seconds)
    {
        var minutes = (int)Math.Round(seconds / 60.0);
        return minutes >= 120 ? $"{minutes / 60} h{(minutes % 60 > 0 ? $" {minutes % 60} min" : "")}"
            : minutes >= 60 ? $"1 h{(minutes % 60 > 0 ? $" {minutes % 60} min" : "")}"
            : $"{minutes} min";
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB".Replace(',', '.')
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB"
        : $"{bytes / 1024.0:0} KB";

    private static string SystemDrive() =>
        Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

    private static long FreeBytes(string drive)
    {
        try
        {
            return new DriveInfo(drive).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    private static string System32(string file)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32");
        return Path.Combine(dir, file);
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Apaga arquivos (e pastas vazias) mais antigos que <paramref name="olderThan"/>. Ignora os que estão em uso.</summary>
    private static (int Files, long Bytes) DeleteContents(IEnumerable<string> directories, TimeSpan olderThan, CancellationToken ct, string? keep = null)
    {
        var files = 0;
        var bytes = 0L;
        var limit = DateTime.Now - olderThan;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", options))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if ((keep is not null && info.Name.Equals(keep, StringComparison.OrdinalIgnoreCase)) ||
                            info.LastWriteTime > limit)
                        {
                            continue;
                        }
                        var length = info.Length;
                        info.Attributes = FileAttributes.Normal;
                        info.Delete();
                        files++;
                        bytes += length;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Em uso ou sem permissão.
                    }
                }

                foreach (var sub in Directory.EnumerateDirectories(directory, "*", options).OrderByDescending(x => x.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(sub).Any())
                        {
                            Directory.Delete(sub);
                        }
                    }
                    catch
                    {
                        // Em uso.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Pasta inacessível.
            }
        }

        return (files, bytes);
    }

    private static bool RenameToBackup(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return true;
            }
            var backup = path + ".old";
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }
            Directory.Move(path, backup);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? DeviceName(string instanceId) =>
        ToText(Get(Wmi($"SELECT Name FROM Win32_PnPEntity WHERE PNPDeviceID = '{WqlEscape(instanceId)}'").FirstOrDefault(), "Name"));

    [SupportedOSPlatform("windows")]
    private static long? DeviceErrorCode(string instanceId) =>
        ToLong(Get(Wmi($"SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPDeviceID = '{WqlEscape(instanceId)}'").FirstOrDefault(), "ConfigManagerErrorCode"));

    private static string WqlEscape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static string Normalize(string text)
    {
        var decomposed = text.Replace("\0", string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    /// <summary>Últimas linhas úteis da saída (sem as linhas de progresso).</summary>
    private static string? Tail(string output, int lines = 6)
    {
        var useful = output
            .Replace("\0", string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 1 && !x.All(c => char.IsDigit(c) || c is '%' or '.' or '=' or '[' or ']' or ' ' or ','))
            .TakeLast(lines)
            .ToList();
        if (useful.Count == 0)
        {
            return null;
        }
        var text = string.Join(" · ", useful);
        return text.Length > 700 ? text[^700..] : text;
    }

    /// <summary>Executa um programa com argumentos separados (sem shell), tempo limite e cancelamento.</summary>
    private static async Task<(int ExitCode, string Output)> Tool(string fileName, IEnumerable<string> arguments, int timeoutSeconds, CancellationToken ct, Encoding? encoding = null)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding ?? ConsoleEncoding(),
                StandardErrorEncoding = encoding ?? ConsoleEncoding()
            };
            foreach (var argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }
            if (!OperatingSystem.IsWindows())
            {
                psi.Environment["LC_ALL"] = "C";
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, string.Empty);
            }

            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Já terminou.
                }
                ct.ThrowIfCancellationRequested();
                return (-1, string.Empty);
            }

            return (process.ExitCode, await output + await error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static Encoding? _consoleEncoding;

    /// <summary>As ferramentas do Windows escrevem na página de código OEM (850 em pt-BR).</summary>
    private static Encoding ConsoleEncoding()
    {
        if (_consoleEncoding is not null)
        {
            return _consoleEncoding;
        }

        if (!OperatingSystem.IsWindows())
        {
            return _consoleEncoding = Encoding.UTF8;
        }

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return _consoleEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return _consoleEncoding = Encoding.UTF8;
        }
    }
}
