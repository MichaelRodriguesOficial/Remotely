using System.Globalization;
using System.Text.RegularExpressions;

namespace Remotely.Shared.Models.Diagnostics;

/// <summary>O que o reparo precisa receber além do ID.</summary>
public enum RepairTargetKind
{
    None,

    /// <summary>Letra de unidade ("C:").</summary>
    Drive,

    /// <summary>ID de instância do dispositivo (PnP).</summary>
    Device,

    /// <summary>Quando reiniciar: minutos ("5") ou horário ("22:00").</summary>
    RebootTime
}

/// <summary>
/// Lista fechada de reparos que o agente sabe executar. Nada de comando livre:
/// o painel só manda o ID e um alvo validado aqui e no agente.
/// </summary>
public static class DiagnosticRepairs
{
    public const string Prefix = "repair:";

    public const string Reboot = "reboot";
    public const string CancelReboot = "cancel-reboot";
    public const string DiskScan = "disk-scan";
    public const string DiskRepairOnBoot = "disk-repair-boot";
    public const string Cleanup = "cleanup";
    public const string SystemFiles = "system-files";
    public const string NetworkFlush = "network-flush";
    public const string NetworkReset = "network-reset";
    public const string WindowsUpdateReset = "windows-update-reset";
    public const string FirewallEnable = "firewall-enable";
    public const string DefenderUpdate = "defender-update";
    public const string TimeSync = "time-sync";
    public const string DeviceRestart = "device-restart";
    public const string DeviceRescan = "device-rescan";

    public sealed record RepairInfo(
        string Id,
        string Name,
        string Description,
        string Icon,
        string Duration,
        RepairTargetKind Target = RepairTargetKind.None,
        bool Reboots = false,
        bool NeedsRebootAfter = false,
        bool Linux = false,
        string[]? Retest = null)
    {
        public string[] RetestTests => Retest ?? [];
    }

    public static IReadOnlyList<RepairInfo> All { get; } =
    [
        new(Reboot, "Reiniciar o computador",
            "Reinicia com aviso na tela do usuário, agora ou num horário. Conclui atualizações e instalações pendentes.",
            "fa-solid fa-power-off", "o tempo escolhido", RepairTargetKind.RebootTime, Reboots: true, Linux: true),
        new(CancelReboot, "Cancelar reinício agendado",
            "Cancela um reinício agendado pelo Remotely ou por outro programa.",
            "fa-solid fa-ban", "segundos", Linux: true),
        new(DiskScan, "Verificar o disco (chkdsk)",
            "Procura erros no sistema de arquivos sem reiniciar (chkdsk /scan) e corrige o que der com a unidade em uso.",
            "fa-solid fa-hard-drive", "1 a 15 minutos", RepairTargetKind.Drive, Retest: [DiagnosticTests.Disks]),
        new(DiskRepairOnBoot, "Reparar o disco no próximo reinício",
            "Agenda a verificação completa (chkdsk /f) para o próximo início do Windows. A inicialização demora mais.",
            "fa-solid fa-hard-drive", "segundos (o reparo roda no próximo reinício)", RepairTargetKind.Drive, NeedsRebootAfter: true),
        new(Cleanup, "Limpeza de disco",
            "Apaga temporários antigos, esvazia a lixeira, limpa o cache do Windows Update e componentes substituídos.",
            "fa-solid fa-broom", "2 a 15 minutos", Linux: true, Retest: [DiagnosticTests.Disks]),
        new(SystemFiles, "Reparar arquivos do Windows",
            "Restaura a imagem do Windows (DISM /RestoreHealth) e verifica os arquivos de sistema (sfc /scannow).",
            "fa-solid fa-shield-halved", "15 a 40 minutos", Retest: [DiagnosticTests.System]),
        new(NetworkFlush, "Limpar DNS e renovar IP",
            "Limpa o cache de DNS e renova o endereço IP. A conexão pode cair por alguns segundos.",
            "fa-solid fa-wifi", "até 1 minuto", Linux: true, Retest: [DiagnosticTests.Network]),
        new(NetworkReset, "Redefinir a rede (Winsock e TCP/IP)",
            "Restaura as configurações de rede do Windows. Use quando a internet não funciona mesmo com o cabo/Wi-Fi ok.",
            "fa-solid fa-network-wired", "segundos", NeedsRebootAfter: true),
        new(WindowsUpdateReset, "Destravar o Windows Update",
            "Reinicia os serviços de atualização, limpa o cache e pede uma nova busca por atualizações.",
            "fa-solid fa-rotate", "1 a 3 minutos", Retest: [DiagnosticTests.System]),
        new(FirewallEnable, "Ativar o firewall",
            "Liga o Firewall do Windows em todos os perfis (domínio, privado e público).",
            "fa-solid fa-fire-flame-curved", "segundos", Retest: [DiagnosticTests.System]),
        new(DefenderUpdate, "Atualizar o Microsoft Defender",
            "Baixa as definições mais recentes do antivírus do Windows.",
            "fa-solid fa-shield-virus", "1 a 5 minutos", Retest: [DiagnosticTests.System]),
        new(TimeSync, "Sincronizar o relógio",
            "Ativa e força a sincronização da hora com o servidor de horário.",
            "fa-solid fa-clock", "segundos", Linux: true),
        new(DeviceRestart, "Reiniciar o dispositivo",
            "Desliga e liga de novo o dispositivo com erro (como desativar e ativar no Gerenciador de Dispositivos).",
            "fa-solid fa-plug-circle-bolt", "segundos", RepairTargetKind.Device, Retest: [DiagnosticTests.Devices]),
        new(DeviceRescan, "Procurar alterações de hardware",
            "Faz o Windows detectar de novo os dispositivos e instalar drivers que já estejam disponíveis.",
            "fa-solid fa-magnifying-glass", "segundos", Retest: [DiagnosticTests.Devices]),
    ];

    public static RepairInfo? Get(string? id) => All.FirstOrDefault(x => x.Id == id);

    public static bool IsRepairRun(string? tests) => tests?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    public static string ToTestId(string repairId) => Prefix + repairId;

    public static RepairInfo? FromTestId(string? testId) =>
        testId?.StartsWith(Prefix, StringComparison.Ordinal) == true ? Get(testId[Prefix.Length..]) : null;

    public static bool IsAvailable(RepairInfo repair, string? platform) =>
        string.Equals(platform, "Windows", StringComparison.OrdinalIgnoreCase) ||
        (repair.Linux && string.Equals(platform, "Linux", StringComparison.OrdinalIgnoreCase));

    /// <summary>Opções de quando reiniciar (valor, texto).</summary>
    public static IReadOnlyList<(string Value, string Label)> RebootOptions { get; } =
    [
        ("2", "Em 2 minutos"),
        ("5", "Em 5 minutos"),
        ("15", "Em 15 minutos"),
        ("30", "Em 30 minutos"),
        ("60", "Em 1 hora"),
    ];

    /// <summary>Valida o alvo do reparo. Devolve o valor normalizado ou null se inválido.</summary>
    public static string? NormalizeTarget(RepairInfo repair, string? target)
    {
        target = target?.Trim();
        switch (repair.Target)
        {
            case RepairTargetKind.None:
                return string.Empty;

            case RepairTargetKind.Drive:
                return target is not null && _driveRegex.IsMatch(target) ? target.ToUpperInvariant() : null;

            case RepairTargetKind.Device:
                return target is not null && _deviceRegex.IsMatch(target) ? target : null;

            case RepairTargetKind.RebootTime:
                if (target is null)
                {
                    return null;
                }
                if (int.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
                {
                    return minutes is >= 1 and <= 24 * 60 ? minutes.ToString(CultureInfo.InvariantCulture) : null;
                }
                return TimeOnly.TryParseExact(target, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                    ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
                    : null;

            default:
                return null;
        }
    }

    /// <summary>Segundos até o reinício pedido (minutos ou próximo horário HH:mm).</summary>
    public static int RebootDelaySeconds(string target, DateTime now)
    {
        if (int.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
        {
            return minutes * 60;
        }

        var time = TimeOnly.ParseExact(target, "HH:mm", CultureInfo.InvariantCulture);
        var when = now.Date + time.ToTimeSpan();
        if (when <= now.AddMinutes(1))
        {
            when = when.AddDays(1);
        }
        return (int)Math.Ceiling((when - now).TotalSeconds);
    }

    /// <summary>Texto curto do alvo para o painel ("C:", "às 22:00", "em 5 min").</summary>
    public static string DescribeTarget(RepairInfo repair, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        return repair.Target switch
        {
            RepairTargetKind.RebootTime => target.Contains(':') ? $"às {target}" : $"em {target} min",
            _ => target
        };
    }

    private static readonly Regex _driveRegex = new(@"^[A-Za-z]:\z", RegexOptions.CultureInvariant);

    // IDs de instância PnP: PCI\VEN_8086&DEV_..., USB\VID_..\5&2C..&0&1, ACPI\..., SWD\...
    private static readonly Regex _deviceRegex = new(@"^[A-Za-z0-9\\&_.#{}\- ]{3,200}\z", RegexOptions.CultureInvariant);
}
