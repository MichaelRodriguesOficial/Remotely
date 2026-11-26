using Microsoft.AspNetCore.Components;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using System.Text;

namespace Remotely.Server.Components.Devices;

/// <summary>
/// Lista de dispositivos no estilo do GLPI, com busca inteligente:
/// texto livre procura em todos os campos e no inventário (softwares, IPs...);
/// "campo:valor" filtra um campo; "-" na frente exclui; aspas agrupam frases.
/// </summary>
public partial class DeviceInventoryList : AuthComponentBase, IDisposable
{
    private const int PageSize = 50;

    private readonly List<Row> _all = [];
    private List<Row> _filtered = [];
    private List<Criterion> _criteria = [];
    private Dictionary<string, string> _groups = [];
    private CancellationTokenSource? _searchCts;
    private UserNameDisplayFormat _nameFormat;
    private string _query = string.Empty;
    private string _status = string.Empty;
    private string _groupId = string.Empty;
    private string _sortKey = "name";
    private bool _sortDescending;
    private bool _isLoading = true;
    private bool _isSearching;
    private int _page = 1;
    private int _searchVersion;

    [Inject]
    private IDataService DataService { get; init; } = null!;

    [Inject]
    private Remotely.Server.Hubs.ICircuitConnection CircuitConnection { get; init; } = null!;

    [Inject]
    private IJsInterop JsInterop { get; init; } = null!;

    [Inject]
    private IToastService ToastService { get; init; } = null!;

    private readonly HashSet<string> _selected = [];
    private bool _isActing;

    private IEnumerable<Row> PageRows => _filtered.Skip((_page - 1) * PageSize).Take(PageSize);

    private bool AllPageSelected => PageRows.Any() && PageRows.All(x => _selected.Contains(x.Device.ID));

    private List<Device> SelectedDevices => _all.Where(x => _selected.Contains(x.Device.ID)).Select(x => x.Device).ToList();

    private void ToggleSelected(string deviceId, bool selected)
    {
        if (selected)
        {
            _selected.Add(deviceId);
        }
        else
        {
            _selected.Remove(deviceId);
        }
    }

    private void TogglePage(bool selected)
    {
        foreach (var row in PageRows)
        {
            ToggleSelected(row.Device.ID, selected);
        }
    }

    private void SelectFiltered()
    {
        foreach (var row in _filtered)
        {
            _selected.Add(row.Device.ID);
        }
    }

    private async Task RefreshSelected()
    {
        var online = SelectedDevices.Where(x => x.IsOnline).ToList();
        if (online.Count == 0)
        {
            ToastService.ShowToast2("Nenhum dos computadores selecionados está online.", Enums.ToastType.Warning);
            return;
        }

        _isActing = true;
        try
        {
            var sent = 0;
            foreach (var device in online)
            {
                if (await CircuitConnection.RequestInventory(device.ID))
                {
                    sent++;
                }
            }
            var offline = _selected.Count - online.Count;
            ToastService.ShowToast2(
                $"Coleta de inventário pedida a {sent} computador(es)." +
                (offline > 0 ? $" {offline} offline fica(m) de fora." : "") +
                " Os dados chegam em até 1 minuto; atualize a página para ver.",
                Enums.ToastType.Info);
        }
        finally
        {
            _isActing = false;
        }
    }

    private async Task WakeSelected()
    {
        var offline = SelectedDevices.Where(x => !x.IsOnline).ToArray();
        if (offline.Length == 0)
        {
            ToastService.ShowToast2("Todos os selecionados já estão online.", Enums.ToastType.Info);
            return;
        }

        var result = await CircuitConnection.WakeDevices(offline);
        ToastService.ShowToast2(
            result.IsSuccess ? $"Wake-on-LAN enviado para {offline.Length} computador(es)." : $"Falha no Wake-on-LAN: {result.Reason}",
            result.IsSuccess ? Enums.ToastType.Success : Enums.ToastType.Warning);
    }

    private async Task PowerSelected(bool restart)
    {
        _isActing = true;
        try
        {
            await PowerActionPrompt.Run(JsInterop, CircuitConnection, ToastService, SelectedDevices, restart);
        }
        finally
        {
            _isActing = false;
        }
    }

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));

    private sealed class Row
    {
        public required Device Device { get; init; }
        public required string UserDisplay { get; init; }
        public required string OsName { get; init; }
        public required string GroupName { get; init; }
        public required string LastSeen { get; init; }
    }

    private sealed record FieldInfo(
        string[] Keys,
        string Label,
        Func<Row, IEnumerable<string?>> Values,
        bool SearchInventory = false,
        bool ShowInMenu = true);

    private sealed record Criterion(FieldInfo? Field, string Value, bool Negate);

    private sealed record ColumnInfo(string Key, string Label, Func<Row, IComparable?> SortValue);

    private static readonly FieldInfo[] Fields =
    [
        new(["nome", "name", "computador"], "Nome", r => [r.Device.DeviceName, r.Device.Alias]),
        new(["usuario", "user"], "Usuário", r => [r.Device.CurrentUser, r.UserDisplay, r.Device.CurrentUserGivenName, r.Device.CurrentUserSurname]),
        new(["local", "localizacao"], "Localização", r => [r.Device.Location]),
        new(["status"], "Status (online/offline)", r => [r.Device.IsOnline ? "online" : "offline"]),
        new(["serie", "serial"], "Número de série", r => [r.Device.SerialNumber]),
        new(["tipo"], "Tipo", r => [r.Device.DeviceType]),
        new(["fabricante"], "Fabricante", r => [r.Device.Manufacturer]),
        new(["modelo"], "Modelo", r => [r.Device.Model]),
        new(["so", "sistema"], "Sistema operacional", r => [r.Device.OSDescription, r.OsName, r.Device.Platform]),
        new(["processador", "cpu"], "Processador", r => [r.Device.ProcessorName]),
        new(["patrimonio", "inventario"], "Número de inventário", r => [r.Device.AssetTag]),
        new(["grupo"], "Grupo", r => [r.GroupName]),
        new(["tag"], "Tags", r => [r.Device.Tags]),
        new(["software", "programa", "app"], "Software instalado", _ => [], SearchInventory: true),
        new(["ip"], "Endereço IP", r => [r.Device.PublicIP], SearchInventory: true),
        new(["mac"], "Endereço MAC", r => r.Device.MacAddresses ?? [], SearchInventory: true),
        new(["antivirus"], "Antivírus", _ => [], SearchInventory: true),
        new(["agente", "versao"], "Versão do agente", r => [r.Device.AgentVersion]),
        new(["id"], "ID do dispositivo", r => [r.Device.ID], ShowInMenu: false),
    ];

    private static readonly ColumnInfo[] Columns =
    [
        new("name", "Nome", r => r.Device.DeviceName),
        new("user", "Usuário", r => r.UserDisplay),
        new("location", "Localização", r => r.Device.Location),
        new("status", "Status", r => r.Device.IsOnline ? 0 : 1),
        new("serial", "Número de série", r => r.Device.SerialNumber),
        new("type", "Tipo", r => r.Device.DeviceType),
        new("manufacturer", "Fabricante", r => r.Device.Manufacturer),
        new("model", "Modelo", r => r.Device.Model),
        new("os", "Sistema operacional", r => r.OsName),
        new("cpu", "Processador", r => r.Device.ProcessorName),
        new("memory", "Memória", r => r.Device.TotalMemory),
        new("disk", "Disco", r => r.Device.TotalStorage),
        new("asset", "Nº de inventário", r => r.Device.AssetTag),
        new("group", "Grupo", r => r.GroupName),
        new("agent", "Agente", r => r.Device.AgentVersion),
        new("inventory", "Último inventário", r => r.Device.InventoryUpdatedAt),
        new("ip", "IP público", r => r.Device.PublicIP),
        new("tags", "Tags", r => r.Device.Tags),
        new("lastseen", "Última conexão", r => r.Device.LastOnline),
    ];

    /// <summary>Colunas que existem mas começam ocultas.</summary>
    private static readonly HashSet<string> HiddenByDefault = ["ip", "tags", "lastseen"];

    private List<string> _visibleColumns = DefaultColumns();
    private string? _dragKey;

    private static List<string> DefaultColumns() =>
        Columns.Skip(1).Where(x => !HiddenByDefault.Contains(x.Key)).Select(x => x.Key).ToList();

    /// <summary>"Nome" sempre primeiro; o resto na ordem escolhida pelo usuário.</summary>
    private IEnumerable<ColumnInfo> VisibleColumns =>
        [Columns[0], .. _visibleColumns.Select(key => Columns.First(c => c.Key == key))];

    private IEnumerable<(ColumnInfo Column, bool Visible)> ColumnChoices =>
        VisibleColumns.Skip(1).Select(x => (x, true))
            .Concat(Columns.Skip(1).Where(x => !_visibleColumns.Contains(x.Key)).Select(x => (x, false)));

    private void LoadColumns()
    {
        var saved = User?.UserOptions?.InventoryColumns;
        if (saved is null)
        {
            _visibleColumns = DefaultColumns();
            return;
        }

        var valid = Columns.Skip(1).Select(x => x.Key).ToHashSet();
        _visibleColumns = saved.Where(valid.Contains).Distinct().ToList();
    }

    private async Task SaveColumns()
    {
        if (User is null)
        {
            return;
        }

        User.UserOptions ??= new Remotely.Shared.Models.RemotelyUserOptions();
        User.UserOptions.InventoryColumns = _visibleColumns.SequenceEqual(DefaultColumns()) ? null : _visibleColumns.ToList();
        await DataService.UpdateUserOptions($"{User.UserName}", User.UserOptions);
    }

    private async Task ToggleColumn(string key, bool visible)
    {
        if (visible && !_visibleColumns.Contains(key))
        {
            _visibleColumns.Add(key);
        }
        else if (!visible)
        {
            _visibleColumns.Remove(key);
        }
        await SaveColumns();
    }

    private async Task MoveColumn(string key, int delta)
    {
        var index = _visibleColumns.IndexOf(key);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _visibleColumns.Count)
        {
            return;
        }
        (_visibleColumns[index], _visibleColumns[target]) = (_visibleColumns[target], _visibleColumns[index]);
        await SaveColumns();
    }

    private async Task DropColumn(string targetKey)
    {
        var key = _dragKey;
        _dragKey = null;
        if (key is null || key == targetKey || !_visibleColumns.Contains(key) || !_visibleColumns.Contains(targetKey))
        {
            return;
        }

        var from = _visibleColumns.IndexOf(key);
        var to = _visibleColumns.IndexOf(targetKey);
        _visibleColumns.RemoveAt(from);
        _visibleColumns.Insert(to, key);
        await SaveColumns();
    }

    private async Task ResetColumns()
    {
        _visibleColumns = DefaultColumns();
        await SaveColumns();
    }

    private static readonly string[] ExportColumns =
    [
        "Nome", "Apelido", "Usuário", "Localização", "Status", "Última conexão", "Número de série", "Tipo",
        "Fabricante", "Modelo", "Sistema operacional", "Processador", "Memória", "Disco", "Nº de inventário",
        "Grupo", "Tags", "IP público", "Versão do agente", "Último inventário"
    ];

    private IEnumerable<IReadOnlyList<string?>> ExportRows() =>
        _filtered.Select(r => (IReadOnlyList<string?>)
        [
            r.Device.DeviceName,
            r.Device.Alias,
            r.UserDisplay,
            r.Device.Location,
            r.Device.IsOnline ? "Online" : "Offline",
            r.Device.LastOnline == default ? null : r.Device.LastOnline.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            r.Device.SerialNumber,
            r.Device.DeviceType,
            r.Device.Manufacturer,
            r.Device.Model,
            r.OsName,
            r.Device.ProcessorName,
            DeviceComputerInfo.FormatGb(r.Device.TotalMemory),
            DeviceComputerInfo.FormatGb(r.Device.TotalStorage),
            r.Device.AssetTag,
            r.GroupName,
            r.Device.Tags,
            r.Device.PublicIP,
            r.Device.AgentVersion,
            r.Device.InventoryUpdatedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
        ]);

    private string ExportSubtitle =>
        string.IsNullOrWhiteSpace(_query) && string.IsNullOrEmpty(_status) && string.IsNullOrEmpty(_groupId)
            ? $"{_filtered.Count} dispositivo(s)"
            : $"{_filtered.Count} de {_all.Count} dispositivo(s) · filtro: " +
              string.Join(" · ", new[]
              {
                  _query,
                  string.IsNullOrEmpty(_status) ? null : $"status {_status}",
                  !string.IsNullOrEmpty(_groupId) && _groups.TryGetValue(_groupId, out var group) ? $"grupo {group}" : null
              }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private string BuildListHtml() =>
        ReportBuilder.TableHtml("Inventário de computadores", ExportSubtitle, ExportColumns, ExportRows());

    private byte[] BuildListCsv() => ReportBuilder.Csv(ExportColumns, ExportRows());

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        EnsureUserSet();

        _nameFormat = (await DataService.GetSettings()).UserNameDisplayFormat;
        LoadColumns();
        _groups = DataService.GetDeviceGroups(UserName)
            .GroupBy(x => x.ID)
            .ToDictionary(x => x.Key, x => x.First().Name);

        foreach (var device in DataService.GetDevicesForUser(UserName).DistinctBy(x => x.ID))
        {
            var (userName, fullName) = DeviceDisplay.GetUser(device, _nameFormat);
            _all.Add(new Row
            {
                Device = device,
                UserDisplay = fullName ?? userName,
                OsName = DeviceDisplay.GetOperatingSystem(device).Name,
                GroupName = device.DeviceGroupID is { } groupId && _groups.TryGetValue(groupId, out var name) ? name : string.Empty,
                LastSeen = device.LastOnline == default ? "Offline" : device.LastOnline.ToLocalTime().ToString("dd/MM/yyyy")
            });
        }

        await ApplyFilters();
        _isLoading = false;
    }

    private void OnQueryInput(ChangeEventArgs args)
    {
        _query = $"{args.Value}";

        // Espera o usuário parar de digitar antes de buscar no inventário.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                await InvokeAsync(async () =>
                {
                    await ApplyFilters();
                    StateHasChanged();
                });
            }
            catch (TaskCanceledException)
            {
                // Nova tecla digitada.
            }
        }, token);
    }

    private async Task ClearQuery()
    {
        _query = string.Empty;
        await ApplyFilters();
    }

    private async Task AddField(FieldInfo field)
    {
        _query = (_query.TrimEnd() + $" {field.Keys[0]}:").TrimStart();
        await ApplyFilters();
    }

    private void SortBy(string key)
    {
        if (_sortKey == key)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortKey = key;
            _sortDescending = false;
        }
        Sort();
    }

    private async Task ApplyFilters()
    {
        var version = ++_searchVersion;
        _criteria = ParseQuery(_query);

        // Termos que também são procurados no inventário (softwares, IPs, componentes...).
        var inventoryTerms = _criteria
            .Where(x => x.Field is null || x.Field.SearchInventory)
            .Select(x => x.Value)
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var inventoryMatches = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (inventoryTerms.Count > 0)
        {
            _isSearching = true;
            var deviceIds = _all.Select(x => x.Device.ID).ToHashSet();
            foreach (var term in inventoryTerms)
            {
                inventoryMatches[term] = await DataService.SearchDeviceInventories(deviceIds, term);
            }
            _isSearching = false;
        }

        if (version != _searchVersion)
        {
            // Uma busca mais nova já começou.
            return;
        }

        IEnumerable<Row> rows = _all;

        if (_status == "online")
        {
            rows = rows.Where(x => x.Device.IsOnline);
        }
        else if (_status == "offline")
        {
            rows = rows.Where(x => !x.Device.IsOnline);
        }

        if (!string.IsNullOrEmpty(_groupId))
        {
            rows = rows.Where(x => x.Device.DeviceGroupID == _groupId);
        }

        foreach (var criterion in _criteria)
        {
            var current = criterion;
            rows = rows.Where(row => Matches(row, current, inventoryMatches) != current.Negate);
        }

        _filtered = rows.ToList();
        _page = 1;
        Sort();
    }

    private static bool Matches(Row row, Criterion criterion, Dictionary<string, HashSet<string>> inventoryMatches)
    {
        var term = Fold(criterion.Value);

        if (criterion.Field is { } field)
        {
            if (field.Keys[0] == "status")
            {
                return term.StartsWith("on") ? row.Device.IsOnline : term.StartsWith("off") ? !row.Device.IsOnline : true;
            }

            if (field.Values(row).Any(value => Fold(value).Contains(term)))
            {
                return true;
            }

            return field.SearchInventory && InInventory(row, criterion.Value, inventoryMatches);
        }

        // Texto livre: qualquer campo do dispositivo ou do inventário.
        foreach (var anyField in Fields)
        {
            if (anyField.Keys[0] != "status" && anyField.Values(row).Any(value => Fold(value).Contains(term)))
            {
                return true;
            }
        }

        if (Fold(row.Device.Notes).Contains(term))
        {
            return true;
        }

        return InInventory(row, criterion.Value, inventoryMatches);
    }

    private static bool InInventory(Row row, string value, Dictionary<string, HashSet<string>> inventoryMatches) =>
        inventoryMatches.TryGetValue(value, out var ids) && ids.Contains(row.Device.ID);

    private void Sort()
    {
        var column = Columns.FirstOrDefault(x => x.Key == _sortKey) ?? Columns[0];
        var comparer = Comparer<IComparable?>.Create((a, b) =>
        {
            // Vazios sempre no fim.
            if (a is null || a is string { Length: 0 })
            {
                return b is null || b is string { Length: 0 } ? 0 : 1;
            }
            if (b is null || b is string { Length: 0 })
            {
                return -1;
            }
            return a is string sa && b is string sb
                ? string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase)
                : a.CompareTo(b);
        });

        var ordered = _filtered.OrderBy(column.SortValue, comparer);
        if (_sortDescending)
        {
            // Mantém os vazios no fim mesmo em ordem decrescente.
            ordered = _filtered.OrderBy(x => column.SortValue(x) is null or string { Length: 0 })
                .ThenByDescending(column.SortValue, Comparer<IComparable?>.Create((a, b) =>
                    a is string sa && b is string sb
                        ? string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase)
                        : (a, b) switch
                        {
                            (null, null) => 0,
                            (null, _) => -1,
                            (_, null) => 1,
                            _ => a.CompareTo(b)
                        }));
        }

        _filtered = ordered.ToList();
    }

    /// <summary>
    /// Divide a busca em critérios: palavras soltas, "frases entre aspas",
    /// campo:valor, campo:"valor com espaço" e -termo para excluir.
    /// </summary>
    private static List<Criterion> ParseQuery(string query)
    {
        var criteria = new List<Criterion>();
        foreach (var token in Tokenize(query))
        {
            var text = token;
            var negate = false;
            if (text.Length > 1 && text[0] == '-')
            {
                negate = true;
                text = text[1..];
            }

            FieldInfo? field = null;
            var colon = text.IndexOf(':');
            if (colon > 0)
            {
                var key = Fold(text[..colon]);
                field = Fields.FirstOrDefault(x => x.Keys.Contains(key));
                if (field is not null)
                {
                    text = text[(colon + 1)..];
                }
            }

            text = text.Trim('"').Trim();
            if (text.Length == 0)
            {
                continue;
            }

            criteria.Add(new Criterion(field, text, negate));
        }
        return criteria;
    }

    private static IEnumerable<string> Tokenize(string query)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in query)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    /// <summary>Minúsculas e sem acentos ("Localização" → "localizacao").</summary>
    private static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            builder.Append(c switch
            {
                'á' or 'à' or 'â' or 'ã' or 'ä' => 'a',
                'é' or 'è' or 'ê' or 'ë' => 'e',
                'í' or 'ì' or 'î' or 'ï' => 'i',
                'ó' or 'ò' or 'ô' or 'õ' or 'ö' => 'o',
                'ú' or 'ù' or 'û' or 'ü' => 'u',
                'ç' => 'c',
                'ñ' => 'n',
                _ => c
            });
        }
        return builder.ToString();
    }

    public new void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        base.Dispose();
    }
}
