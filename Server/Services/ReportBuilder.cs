using Remotely.Server.Components.Devices;
using Remotely.Server.Models;
using Remotely.Shared.Entities;
using Remotely.Shared.Models.Diagnostics;
using Remotely.Shared.Models.Inventory;
using System.Globalization;
using System.Net;
using System.Text;

namespace Remotely.Server.Services;

/// <summary>
/// Relatórios para exportar e enviar por e-mail (HTML para imprimir/PDF e e-mail,
/// CSV para Excel). Todo texto vindo dos dispositivos passa por HtmlEncode.
/// </summary>
public static class ReportBuilder
{
    // ---------------------------------------------------------------
    // HTML
    // ---------------------------------------------------------------

    private const string Styles = """
        body { font-family: Segoe UI, Arial, Helvetica, sans-serif; color: #1f2937; margin: 24px; font-size: 13px; }
        h1 { font-size: 22px; margin: 0 0 4px; }
        h2 { font-size: 16px; margin: 22px 0 8px; padding-bottom: 4px; border-bottom: 2px solid #e5e7eb; }
        h3 { font-size: 13px; margin: 14px 0 6px; color: #4b5563; text-transform: uppercase; letter-spacing: .04em; }
        .meta { color: #6b7280; margin-bottom: 16px; }
        table { border-collapse: collapse; width: 100%; margin-bottom: 8px; }
        th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid #e5e7eb; vertical-align: top; }
        th { background: #f3f4f6; font-size: 11px; text-transform: uppercase; letter-spacing: .03em; color: #4b5563; }
        td.label { width: 32%; color: #4b5563; }
        table.items { table-layout: fixed; }
        td.status { width: 90px; }
        .hint { color: #6b7280; font-size: 12px; }
        .badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 11px; font-weight: 600; }
        .ok { background: #dcfce7; color: #166534; }
        .warning { background: #fef3c7; color: #92400e; }
        .error { background: #fee2e2; color: #991b1b; }
        .info, .skipped, .pending, .running { background: #e5e7eb; color: #374151; }
        .summary { display: flex; gap: 12px; flex-wrap: wrap; margin: 8px 0 4px; }
        .summary div { border: 1px solid #e5e7eb; border-radius: 8px; padding: 8px 12px; min-width: 120px; }
        .summary span { display: block; color: #6b7280; font-size: 11px; text-transform: uppercase; }
        .summary strong { font-size: 15px; }
        .footer { margin-top: 28px; color: #9ca3af; font-size: 11px; }
        @media print { body { margin: 12mm; } h2 { break-after: avoid; } tr { break-inside: avoid; } }
        """;

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Page(string title, string body) => $"""
        <!DOCTYPE html>
        <html lang="pt-BR">
        <head>
        <meta charset="utf-8" />
        <title>{E(title)}</title>
        <style>{Styles}</style>
        </head>
        <body>
        {body}
        <div class="footer">Gerado pelo Remotely em {DateTimeOffset.Now.ToLocalTime():dd/MM/yyyy HH:mm}.</div>
        </body>
        </html>
        """;

    private static string StatusClass(DiagnosticStatus status) => status.ToString().ToLowerInvariant();

    public static string StatusText(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Ok => "Ok",
        DiagnosticStatus.Warning => "Atenção",
        DiagnosticStatus.Error => "Problema",
        DiagnosticStatus.Skipped => "Não se aplica",
        DiagnosticStatus.Pending => "Na fila",
        DiagnosticStatus.Running => "Executando",
        _ => "Info"
    };

    private static string DeviceHeader(Device device, UserNameDisplayFormat nameFormat)
    {
        var (os, version) = DeviceDisplay.GetOperatingSystem(device);
        var (user, fullName) = DeviceDisplay.GetUser(device, nameFormat);
        var cells = new (string Label, string? Value)[]
        {
            ("Tipo", device.DeviceType),
            ("Fabricante", device.Manufacturer),
            ("Modelo", device.Model),
            ("Número de série", device.SerialNumber),
            ("Nº de inventário", device.AssetTag),
            ("Localização", device.Location),
            ("Sistema", version is null ? os : $"{os} ({version})"),
            ("Usuário", fullName is null ? user : $"{fullName} ({user})"),
        };

        var sb = new StringBuilder("<div class=\"summary\">");
        foreach (var (label, value) in cells.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            sb.Append($"<div><span>{E(label)}</span><strong>{E(value)}</strong></div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    public static string DiagnosticsHtml(Device device, DeviceDiagnosticRun run, DiagnosticReport report, UserNameDisplayFormat nameFormat)
    {
        var status = report.Completed ? report.OverallStatus : DiagnosticStatus.Running;
        var sb = new StringBuilder();
        sb.Append($"<h1>Diagnóstico de {E(device.DeviceName)}</h1>");
        sb.Append($"<div class=\"meta\">Executado em {run.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm}");
        if (!string.IsNullOrWhiteSpace(run.RequestedBy))
        {
            sb.Append($" por {E(run.RequestedBy)}");
        }
        sb.Append($" · Resultado: <span class=\"badge {StatusClass(status)}\">{E(StatusText(status))}</span></div>");
        sb.Append(DeviceHeader(device, nameFormat));

        sb.Append("<h2>Resumo</h2><table><tr><th>Teste</th><th>Resultado</th><th>Conclusão</th></tr>");
        foreach (var test in report.Tests)
        {
            sb.Append($"<tr><td>{E(test.Name)}</td><td><span class=\"badge {StatusClass(test.Status)}\">{E(StatusText(test.Status))}</span></td><td>{E(test.Summary)}</td></tr>");
        }
        sb.Append("</table>");

        foreach (var test in report.Tests.Where(x => x.Items.Count > 0))
        {
            sb.Append($"<h2>{E(test.Name)} <span class=\"badge {StatusClass(test.Status)}\">{E(StatusText(test.Status))}</span></h2>");
            foreach (var group in test.Items.GroupBy(x => x.Group))
            {
                if (!string.IsNullOrWhiteSpace(group.Key))
                {
                    sb.Append($"<h3>{E(group.Key)}</h3>");
                }
                sb.Append("<table class=\"items\">");
                foreach (var item in group)
                {
                    sb.Append($"<tr><td class=\"label\">{E(item.Label)}</td><td class=\"status\"><span class=\"badge {StatusClass(item.Status)}\">{E(StatusText(item.Status))}</span></td><td>{E(item.Value)}");
                    if (!string.IsNullOrWhiteSpace(item.Hint))
                    {
                        sb.Append($"<div class=\"hint\">{E(item.Hint)}</div>");
                    }
                    sb.Append("</td></tr>");
                }
                sb.Append("</table>");
            }
        }

        return Page($"Diagnóstico de {device.DeviceName}", sb.ToString());
    }

    public static string InventoryHtml(Device device, DeviceInventory? inventory, UserNameDisplayFormat nameFormat, bool showSensitive)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>Inventário de {E(device.DeviceName)}</h1>");
        sb.Append($"<div class=\"meta\">{(inventory is null ? "Inventário ainda não recebido." : $"Coletado em {inventory.CollectedAt.ToLocalTime():dd/MM/yyyy HH:mm}")}</div>");
        sb.Append(DeviceHeader(device, nameFormat));

        foreach (var section in InventorySections(device, inventory, showSensitive))
        {
            if (section.Rows.Count == 0)
            {
                continue;
            }

            sb.Append($"<h2>{E(section.Title)} <span class=\"badge info\">{section.Rows.Count}</span></h2><table><tr>");
            foreach (var column in section.Columns)
            {
                sb.Append($"<th>{E(column)}</th>");
            }
            sb.Append("</tr>");
            foreach (var row in section.Rows)
            {
                sb.Append("<tr>");
                foreach (var cell in row)
                {
                    sb.Append($"<td>{E(cell)}</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</table>");
        }

        return Page($"Inventário de {device.DeviceName}", sb.ToString());
    }

    public static string TableHtml(string title, string? subtitle, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder($"<h1>{E(title)}</h1>");
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            sb.Append($"<div class=\"meta\">{E(subtitle)}</div>");
        }
        sb.Append("<table><tr>");
        foreach (var column in columns)
        {
            sb.Append($"<th>{E(column)}</th>");
        }
        sb.Append("</tr>");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row)
            {
                sb.Append($"<td>{E(cell)}</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        return Page(title, sb.ToString());
    }

    // ---------------------------------------------------------------
    // CSV (Excel em português: separador ";" e UTF-8 com BOM)
    // ---------------------------------------------------------------

    public static byte[] Csv(IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', columns.Select(CsvCell)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(';', row.Select(CsvCell)));
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string CsvCell(string? value)
    {
        value ??= string.Empty;

        // Evita que o Excel interprete o conteúdo como fórmula (injeção de CSV).
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    public static byte[] DiagnosticsCsv(Device device, DiagnosticReport report) =>
        Csv(
            ["Dispositivo", "Teste", "Resultado do teste", "Grupo", "Item", "Valor", "Status", "Recomendação"],
            report.Tests.SelectMany(test => test.Items.Count == 0
                ? [(IReadOnlyList<string?>)[device.DeviceName, test.Name, StatusText(test.Status), null, null, test.Summary, null, null]]
                : test.Items.Select(item => (IReadOnlyList<string?>)
                    [device.DeviceName, test.Name, StatusText(test.Status), item.Group, item.Label, item.Value, StatusText(item.Status), item.Hint])));

    /// <summary>
    /// Uma tabela por seção (título, cabeçalho e linhas), separadas por uma linha em branco,
    /// para ficar legível ao abrir no Excel.
    /// </summary>
    public static byte[] InventoryCsv(Device device, DeviceInventory? inventory, bool showSensitive)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvCell($"Inventário de {device.DeviceName}"));
        if (inventory is not null)
        {
            sb.AppendLine(CsvCell($"Coletado em {inventory.CollectedAt.ToLocalTime():dd/MM/yyyy HH:mm}"));
        }

        foreach (var section in InventorySections(device, inventory, showSensitive).Where(x => x.Rows.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine(CsvCell(section.Title));
            sb.AppendLine(string.Join(';', section.Columns.Select(CsvCell)));
            foreach (var row in section.Rows)
            {
                sb.AppendLine(string.Join(';', row.Select(CsvCell)));
            }
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    // ---------------------------------------------------------------
    // Seções do inventário (comuns ao HTML e ao CSV)
    // ---------------------------------------------------------------

    private sealed record Section(string Title, IReadOnlyList<string> Columns, List<IReadOnlyList<string?>> Rows);

    private static IEnumerable<Section> InventorySections(Device device, DeviceInventory? inventory, bool showSensitive)
    {
        var os = inventory?.OperatingSystem;
        yield return new Section("Computador", ["Campo", "Valor"],
        [
            .. new (string, string?)[]
            {
                ("Nome", device.DeviceName),
                ("UUID", inventory?.Uuid),
                ("Tipo", device.DeviceType),
                ("Fabricante", device.Manufacturer),
                ("Modelo", device.Model),
                ("Número de série", device.SerialNumber),
                ("Número de inventário", device.AssetTag),
                ("Localização", device.Location),
                ("Domínio", inventory?.Domain),
                ("Sistema operacional", os?.Name),
                ("Versão", os?.DisplayVersion ?? os?.Version),
                ("Build", os?.Build),
                ("Número de série do SO", showSensitive ? os?.SerialNumber : InventoryFormat.MaskProductKey(os?.SerialNumber)),
                ("ID do produto", os?.ProductId),
                ("Licença", os?.LicenseStatus),
                ("BIOS", inventory?.Bios is { } bios ? $"{bios.Manufacturer} {bios.Version} ({bios.ReleaseDate?.ToLocalTime():dd/MM/yyyy})" : null),
                ("Placa-mãe", inventory?.Motherboard is { } board ? $"{board.Manufacturer} {board.Product}" : null),
                ("Memória total", DeviceComputerInfo.FormatGb(device.TotalMemory)),
                ("Armazenamento", DeviceComputerInfo.FormatGb(device.TotalStorage)),
            }
            .Where(x => !string.IsNullOrWhiteSpace(x.Item2))
            .Select(x => (IReadOnlyList<string?>)[x.Item1, x.Item2])
        ]);

        if (inventory is null)
        {
            yield break;
        }

        yield return new Section("Processadores", ["Modelo", "Fabricante", "Núcleos", "Threads", "Frequência máxima", "Frequência base", "Soquete"],
            [.. inventory.Processors.Select(x => (IReadOnlyList<string?>)[x.Name, x.Manufacturer, x.Cores?.ToString(), x.Threads?.ToString(),
                x.MaxClockMhz is > 0 ? $"{x.MaxClockMhz} MHz" : null, x.CurrentClockMhz is > 0 ? $"{x.CurrentClockMhz} MHz" : null, x.Socket])]);

        yield return new Section("Memória", ["Slot", "Capacidade", "Tipo", "Frequência", "Fabricante", "Part number", "Número de série"],
            [.. inventory.Memory.Select(x => (IReadOnlyList<string?>)[x.Slot, x.CapacityBytes is > 0 ? InventoryFormat.Bytes(x.CapacityBytes.Value) : null,
                string.Join(" ", new[] { x.Type, x.FormFactor }.Where(t => t is not null)), x.SpeedMhz is > 0 ? $"{x.SpeedMhz} MHz" : null,
                x.Manufacturer, x.PartNumber, x.SerialNumber])]);

        yield return new Section("Discos", ["Modelo", "Capacidade", "Tipo", "Interface", "Firmware", "Número de série", "Saúde"],
            [.. inventory.Disks.Select(x => (IReadOnlyList<string?>)[x.Model, x.SizeBytes is > 0 ? InventoryFormat.Bytes(x.SizeBytes.Value) : null,
                x.MediaType, x.Interface, x.Firmware, x.SerialNumber, x.Health])]);

        foreach (var group in inventory.Components.Where(x => !string.IsNullOrWhiteSpace(x.Category)).GroupBy(x => x.Category))
        {
            var detailColumns = group.SelectMany(x => x.Details.Keys).Distinct().ToList();
            yield return new Section(group.Key, ["Nome", "Fabricante", "Versão", "Número de série", .. detailColumns],
                [.. group.Select(x => (IReadOnlyList<string?>)[x.Name, x.Manufacturer, x.Version, x.SerialNumber,
                    .. detailColumns.Select(c => x.Details.TryGetValue(c, out var v) ? v : null)])]);
        }

        yield return new Section("Volumes", ["Volume", "Rótulo", "Tipo", "Sistema de arquivos", "Livre", "Total", "Criptografia"],
            [.. inventory.Volumes.Select(x => (IReadOnlyList<string?>)[x.MountPoint, x.Label, x.Type, x.FileSystem,
                x.FreeBytes is > 0 ? InventoryFormat.Bytes(x.FreeBytes.Value) : null, x.TotalBytes is > 0 ? InventoryFormat.Bytes(x.TotalBytes.Value) : null, x.Encryption])]);

        yield return new Section("Portas de rede", ["Nome", "Descrição", "MAC", "IPv4", "Gateway", "DNS", "Conectada"],
            [.. inventory.NetworkPorts.Where(x => x.Virtual != true).Select(x => (IReadOnlyList<string?>)[x.Name, x.Description, x.MacAddress,
                string.Join(", ", x.IPv4), string.Join(", ", x.Gateways), string.Join(", ", x.DnsServers), x.Connected is null ? null : x.Connected.Value ? "Sim" : "Não"])]);

        yield return new Section("Antivírus", ["Nome", "Fabricante", "Versão", "Ativo", "Atualizado"],
            [.. inventory.Antivirus.Select(x => (IReadOnlyList<string?>)[x.Name, x.Manufacturer, x.Version,
                x.Enabled is null ? null : x.Enabled.Value ? "Sim" : "Não", x.UpToDate is null ? null : x.UpToDate.Value ? "Sim" : "Não"])]);

        yield return new Section("Softwares", ["Nome", "Versão", "Editor", "Instalado em", "Arquitetura"],
            [.. inventory.Software.Select(x => (IReadOnlyList<string?>)[x.Name, x.Version, x.Publisher,
                x.InstallDate?.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), x.Architecture])]);
    }
}
