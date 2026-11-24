using Microsoft.AspNetCore.Components;
using Remotely.Server.Components.Devices;
using Remotely.Server.Hubs;
using Remotely.Server.Models;
using Remotely.Server.Models.Messages;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Models.Diagnostics;

namespace Remotely.Server.Components.Pages;

/// <summary>
/// Menu Diagnóstico: situação de todos os computadores pelos diagnósticos mais recentes,
/// com o resultado de cada teste, o último reparo e execução em lote dos testes rápidos.
/// </summary>
public partial class DiagnosticsPage : AuthComponentBase
{
    private const int PageSize = 50;

    private readonly List<Row> _all = [];
    private readonly HashSet<string> _selected = [];
    private List<Row> _filtered = [];
    private Dictionary<string, string> _groups = [];
    private UserNameDisplayFormat _nameFormat;
    private string _query = string.Empty;
    private string _status = string.Empty;
    private string _diagnostic = string.Empty;
    private string _groupId = string.Empty;
    private string _sortKey = "situation";
    private bool _sortDescending;
    private bool _isLoading = true;
    private bool _isStarting;
    private int _page = 1;
    private List<string> _visibleColumns = DefaultColumns();
    private string? _dragKey;
    private CancellationTokenSource? _refreshCts;
    private readonly HashSet<string> _dialogTests = [];
    private bool _testsDialogOpen;
    private DiagnosticRepairs.RepairInfo? _repair;
    private string? _repairTarget;
    private string _rebootChoice = "5";
    private TimeOnly? _rebootTime = new(22, 0);

    [Inject]
    private IDataService DataService { get; init; } = null!;

    [Inject]
    private ICircuitConnection CircuitConnection { get; init; } = null!;

    [Inject]
    private IJsInterop JsInterop { get; init; } = null!;

    [Inject]
    private IToastService ToastService { get; init; } = null!;

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_filtered.Count / (double)PageSize));

    private IEnumerable<Row> PageRows => _filtered.Skip((_page - 1) * PageSize).Take(PageSize);

    private sealed class Row
    {
        public required Device Device { get; set; }
        public required string UserDisplay { get; init; }
        public required string GroupName { get; init; }
        public DeviceDiagnosticSummary? Summary { get; set; }

        /// <summary>Problema, atenção, tudo certo, sem diagnóstico.</summary>
        public int Rank => Summary is not { HasResults: true } ? 3 : Summary.Status switch
        {
            DiagnosticStatus.Error => 0,
            DiagnosticStatus.Warning => 1,
            _ => 2
        };
    }

    private sealed record ColumnInfo(string Key, string Label, Func<Row, IComparable?> SortValue, string? TestId = null);

    private static readonly ColumnInfo[] Columns =
    [
        new("name", "Nome", r => r.Device.DeviceName),
        new("situation", "Situação", r => r.Rank),
        .. DiagnosticTests.All.Select(test => new ColumnInfo(
            test.Id,
            ShortName(test.Id, test.Name),
            r => r.Summary?.Tests.TryGetValue(test.Id, out var state) == true ? StatusRank(state.Status) : 9,
            test.Id)),
        new("issues", "Pendências", r => r.Summary?.Issues.Count ?? 0),
        new("lastrun", "Último diagnóstico", r => r.Summary is { HasResults: true } s ? s.LastRunAt : null),
        new("repair", "Último reparo", r => r.Summary?.LastRepair?.At),
        new("status", "Status", r => r.Device.IsOnline ? 0 : 1),
        new("user", "Usuário", r => r.UserDisplay),
        new("group", "Grupo", r => r.GroupName),
        new("location", "Localização", r => r.Device.Location),
    ];

    private static readonly HashSet<string> HiddenByDefault =
        [DiagnosticTests.DiskSpeed, DiagnosticTests.CpuStress, DiagnosticTests.MemoryTest, "user", "group", "location"];

    private static List<string> DefaultColumns() =>
        Columns.Skip(1).Where(x => !HiddenByDefault.Contains(x.Key)).Select(x => x.Key).ToList();

    private IEnumerable<ColumnInfo> VisibleColumns =>
        [Columns[0], .. _visibleColumns.Select(key => Columns.First(c => c.Key == key))];

    private IEnumerable<(ColumnInfo Column, bool Visible)> ColumnChoices =>
        VisibleColumns.Skip(1).Select(x => (x, true))
            .Concat(Columns.Skip(1).Where(x => !_visibleColumns.Contains(x.Key)).Select(x => (x, false)));

    private static string ShortName(string id, string name) => id switch
    {
        DiagnosticTests.Disks => "Discos",
        DiagnosticTests.Battery => "Bateria",
        DiagnosticTests.Devices => "Dispositivos",
        DiagnosticTests.Network => "Rede",
        DiagnosticTests.System => "Sistema",
        DiagnosticTests.DiskSpeed => "Vel. do disco",
        DiagnosticTests.CpuStress => "Estresse de CPU",
        DiagnosticTests.MemoryTest => "Teste de memória",
        _ => name
    };

    private static int StatusRank(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Error => 0,
        DiagnosticStatus.Warning => 1,
        DiagnosticStatus.Ok => 2,
        _ => 3
    };

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
                GroupName = device.DeviceGroupID is { } groupId && _groups.TryGetValue(groupId, out var name) ? name : string.Empty
            });
        }

        var summaries = await DataService.GetDiagnosticSummaries(_all.Select(x => x.Device.ID).ToList());
        foreach (var row in _all)
        {
            row.Summary = summaries.GetValueOrDefault(row.Device.ID);
        }

        await Register<DiagnosticsUpdatedMessage, string>(CircuitConnection.ConnectionId, HandleDiagnosticsUpdated);

        ApplyFilters();
        _isLoading = false;
    }

    private readonly HashSet<string> _pendingRefresh = [];

    private Task HandleDiagnosticsUpdated(object subscriber, DiagnosticsUpdatedMessage message)
    {
        if (_all.All(x => x.Device.ID != message.DeviceId))
        {
            return Task.CompletedTask;
        }

        // Não segura quem enviou a mensagem (o hub do agente): agrupa as atualizações
        // que chegam em sequência e recarrega em segundo plano.
        lock (_pendingRefresh)
        {
            _pendingRefresh.Add(message.DeviceId);
        }
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();
        _ = RefreshLater(_refreshCts.Token);
        return Task.CompletedTask;
    }

    private async Task RefreshLater(CancellationToken token)
    {
        try
        {
            await Task.Delay(1000, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        List<string> changed;
        lock (_pendingRefresh)
        {
            changed = _pendingRefresh.ToList();
            _pendingRefresh.Clear();
        }

        var ids = _all.Where(x => x.Summary?.IsRunning == true || changed.Contains(x.Device.ID))
            .Select(x => x.Device.ID)
            .ToList();
        try
        {
            var summaries = await DataService.GetDiagnosticSummaries(ids);
            await InvokeAsync(() =>
            {
                foreach (var item in _all.Where(x => ids.Contains(x.Device.ID)))
                {
                    item.Summary = summaries.GetValueOrDefault(item.Device.ID);
                }
                ApplyFilters(resetPage: false);
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // Página fechada enquanto atualizava.
        }
    }

    private void OnQueryInput(ChangeEventArgs args)
    {
        _query = args.Value?.ToString() ?? string.Empty;
        ApplyFilters();
    }

    private void ApplyFilters() => ApplyFilters(resetPage: true);

    private void ApplyFilters(bool resetPage)
    {
        IEnumerable<Row> rows = _all;

        rows = _status switch
        {
            "online" => rows.Where(x => x.Device.IsOnline),
            "offline" => rows.Where(x => !x.Device.IsOnline),
            _ => rows
        };

        if (!string.IsNullOrEmpty(_groupId))
        {
            rows = rows.Where(x => x.Device.DeviceGroupID == _groupId);
        }

        rows = _diagnostic switch
        {
            "issues" => rows.Where(x => x.Rank is 0 or 1),
            "error" => rows.Where(x => x.Rank == 0),
            "warning" => rows.Where(x => x.Rank == 1),
            "ok" => rows.Where(x => x.Rank == 2),
            "never" => rows.Where(x => x.Rank == 3),
            "running" => rows.Where(x => x.Summary?.IsRunning == true),
            _ => rows
        };

        foreach (var term in _query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            rows = rows.Where(x => Matches(x, term));
        }

        _filtered = rows.ToList();
        Sort();
        if (resetPage)
        {
            _page = 1;
        }
        _page = Math.Clamp(_page, 1, TotalPages);
    }

    private static bool Matches(Row row, string term)
    {
        string?[] values =
        [
            row.Device.DeviceName,
            row.Device.Alias,
            row.UserDisplay,
            row.Device.CurrentUser,
            row.GroupName,
            row.Device.Location,
            row.Device.Tags,
            row.Summary?.LastRepair?.Name,
            .. row.Summary?.Issues.Select(x => x.Name) ?? []
        ];
        return values.Any(x => x?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
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
            _sortDescending = key is "lastrun" or "repair" or "issues";
        }
        Sort();
    }

    private void Sort()
    {
        var column = Columns.FirstOrDefault(x => x.Key == _sortKey) ?? Columns[1];
        IComparer<IComparable?> comparer = Comparer<IComparable?>.Create((a, b) =>
            a is null ? (b is null ? 0 : 1) : b is null ? -1 : a.CompareTo(b));

        var ordered = _sortDescending
            ? _filtered.OrderByDescending(column.SortValue, comparer)
            : _filtered.OrderBy(column.SortValue, comparer);
        _filtered = ordered.ThenBy(x => x.Device.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task FilterDiagnostic(string value)
    {
        _diagnostic = _diagnostic == value ? string.Empty : value;
        ApplyFilters();
        await Task.CompletedTask;
    }

    private int Count(int rank) => _all.Count(x => x.Rank == rank);

    // -----------------------------------------------------------------
    // Seleção e execução em lote
    // -----------------------------------------------------------------

    private bool AllPageSelected => PageRows.Any() && PageRows.All(x => _selected.Contains(x.Device.ID));

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

    private List<Row> SelectedRows => _all.Where(x => _selected.Contains(x.Device.ID)).ToList();

    private static string[] QuickTests => DiagnosticTests.All.Where(x => !x.Heavy).Select(x => x.Id).ToArray();

    /// <summary>"Testes rápidos" no menu: roda direto, com uma confirmação simples.</summary>
    private async Task RunQuickTests()
    {
        var online = SelectedRows.Count(x => x.Device.IsOnline);
        var offline = _selected.Count - online;
        if (online == 0)
        {
            ToastService.ShowToast2("Nenhum dos computadores selecionados está online.", Enums.ToastType.Warning);
            return;
        }

        if (!await JsInterop.Confirm(new ConfirmOptions(
            "Executar testes rápidos",
            $"Discos, bateria, dispositivos, rede e sistema em {online} computador(es). Levam segundos e não atrapalham o usuário." +
            (offline == 1 ? " 1 computador offline fica de fora." : offline > 1 ? $" {offline} computadores offline ficam de fora." : ""),
            $"Executar em {online}",
            Icon: "fa-solid fa-bolt")))
        {
            return;
        }

        await StartTests(QuickTests);
    }

    /// <summary>Abre a janela de escolha de testes ("Teste completo" já vem com todos marcados).</summary>
    private void OpenTestsDialog(bool complete)
    {
        _dialogTests.Clear();
        foreach (var test in DiagnosticTests.All.Where(x => complete || !x.Heavy))
        {
            _dialogTests.Add(test.Id);
        }
        _testsDialogOpen = true;
    }

    private void ToggleDialogTest(string id, bool value)
    {
        if (value)
        {
            _dialogTests.Add(id);
        }
        else
        {
            _dialogTests.Remove(id);
        }
    }

    private async Task ConfirmTestsDialog()
    {
        if (_dialogTests.Count == 0)
        {
            return;
        }

        _testsDialogOpen = false;
        await StartTests(DiagnosticTests.All.Where(x => _dialogTests.Contains(x.Id)).Select(x => x.Id).ToArray());
    }

    private async Task StartTests(string[] tests)
    {
        if (!Permissions.ManageDevices)
        {
            ToastService.ShowToast2("Seu perfil não permite executar diagnósticos.", Enums.ToastType.Warning);
            return;
        }

        var targets = SelectedRows.Where(x => x.Device.IsOnline).ToList();
        if (targets.Count == 0)
        {
            ToastService.ShowToast2("Nenhum dos computadores selecionados está online.", Enums.ToastType.Warning);
            return;
        }

        _isStarting = true;
        try
        {
            var started = 0;
            foreach (var row in targets)
            {
                var result = await CircuitConnection.RunDiagnostics(row.Device.ID, tests);
                if (result.IsSuccess)
                {
                    started++;
                }
            }

            ToastService.ShowToast2(
                $"Diagnóstico ({tests.Length} teste(s)) iniciado em {started} computador(es). A lista atualiza sozinha.",
                Enums.ToastType.Info);
            _selected.Clear();
            await ReloadSummaries(targets);
        }
        finally
        {
            _isStarting = false;
        }
    }

    private async Task ReloadSummaries(IReadOnlyCollection<Row> rows)
    {
        var summaries = await DataService.GetDiagnosticSummaries(rows.Select(x => x.Device.ID).ToList());
        foreach (var row in rows)
        {
            row.Summary = summaries.GetValueOrDefault(row.Device.ID);
        }
        ApplyFilters(resetPage: false);
    }

    // -----------------------------------------------------------------
    // Reparos em lote
    // -----------------------------------------------------------------

    private void OpenRepairDialog(DiagnosticRepairs.RepairInfo repair)
    {
        _repair = repair;
        _repairTarget = repair.Target == RepairTargetKind.Drive ? "C:" : null;
        _rebootChoice = "5";
    }

    /// <summary>Selecionados online e com o sistema que o reparo atende.</summary>
    private List<Row> RepairTargets(DiagnosticRepairs.RepairInfo repair) =>
        SelectedRows.Where(x => x.Device.IsOnline && DiagnosticRepairs.IsAvailable(repair, x.Device.Platform)).ToList();

    private async Task ConfirmRepairDialog()
    {
        if (_repair is not { } repair)
        {
            return;
        }

        if (!Permissions.Repairs)
        {
            ToastService.ShowToast2("Seu perfil não permite executar reparos.", Enums.ToastType.Warning);
            return;
        }

        var target = repair.Target switch
        {
            RepairTargetKind.RebootTime => _rebootChoice == "time" ? _rebootTime?.ToString("HH:mm") : _rebootChoice,
            RepairTargetKind.None => null,
            _ => _repairTarget
        };
        if (DiagnosticRepairs.NormalizeTarget(repair, target) is null)
        {
            ToastService.ShowToast2(repair.Target == RepairTargetKind.Drive ? "Informe a unidade, por exemplo C:." : "Escolha quando reiniciar.", Enums.ToastType.Warning);
            return;
        }

        var targets = RepairTargets(repair);
        if (targets.Count == 0)
        {
            ToastService.ShowToast2("Nenhum computador selecionado pode receber este reparo agora.", Enums.ToastType.Warning);
            return;
        }

        _isStarting = true;
        _repair = null;
        try
        {
            var started = 0;
            var failures = new List<string>();
            foreach (var row in targets)
            {
                var result = await CircuitConnection.RunRepair(row.Device.ID, repair.Id, target);
                if (result.IsSuccess)
                {
                    started++;
                }
                else
                {
                    failures.Add(row.Device.DeviceName ?? row.Device.ID);
                }
            }

            ToastService.ShowToast2(
                $"{repair.Name}: iniciado em {started} computador(es)." +
                (failures.Count > 0 ? $" Não iniciou em: {string.Join(", ", failures.Take(5))}{(failures.Count > 5 ? "..." : "")}." : ""),
                failures.Count > 0 ? Enums.ToastType.Warning : Enums.ToastType.Info);
            _selected.Clear();
            await ReloadSummaries(targets);
        }
        finally
        {
            _isStarting = false;
        }
    }

    // -----------------------------------------------------------------
    // Colunas
    // -----------------------------------------------------------------

    private void LoadColumns()
    {
        var saved = User?.UserOptions?.DiagnosticColumns;
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
        User.UserOptions.DiagnosticColumns = _visibleColumns.SequenceEqual(DefaultColumns()) ? null : _visibleColumns.ToList();
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

    // -----------------------------------------------------------------
    // Textos (tela e exportação)
    // -----------------------------------------------------------------

    private static string SituationText(Row row) => row.Rank switch
    {
        0 => "Problema",
        1 => "Atenção",
        2 => "Tudo certo",
        _ => row.Summary?.RunningRepair is not null ? "Reparando" : row.Summary?.IsRunning == true ? "Executando" : "Sem diagnóstico"
    };

    private static string StatusText(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Ok => "Ok",
        DiagnosticStatus.Warning => "Atenção",
        DiagnosticStatus.Error => "Problema",
        DiagnosticStatus.Skipped => "Não se aplica",
        DiagnosticStatus.Running or DiagnosticStatus.Pending => "Executando",
        _ => "Informação"
    };

    private static string RepairStatusText(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Ok => "concluído",
        DiagnosticStatus.Warning => "com avisos",
        DiagnosticStatus.Error => "não concluído",
        DiagnosticStatus.Running or DiagnosticStatus.Pending => "executando",
        _ => "sem resposta"
    };

    private string CellText(Row row, string key)
    {
        var summary = row.Summary;
        if (Columns.FirstOrDefault(x => x.Key == key)?.TestId is { } testId)
        {
            return summary?.Tests.TryGetValue(testId, out var state) == true ? StatusText(state.Status) : "";
        }

        return key switch
        {
            "name" => row.Device.DeviceName,
            "situation" => SituationText(row),
            "issues" => string.Join(", ", summary?.Issues.Select(x => x.Name) ?? []),
            "lastrun" => summary is { HasResults: true } ? summary.LastRunAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "",
            "repair" => summary?.LastRepair is { } repair ? $"{repair.Name} ({RepairStatusText(repair.Status)}, {repair.At.ToLocalTime():dd/MM/yyyy HH:mm})" : "",
            "status" => row.Device.IsOnline ? "Online" : "Offline",
            "user" => row.UserDisplay,
            "group" => row.GroupName,
            "location" => row.Device.Location ?? "",
            _ => ""
        };
    }

    private string ExportSubtitle =>
        $"{_filtered.Count} de {_all.Count} dispositivo(s) · {Count(0)} com problema, {Count(1)} com atenção, {Count(2)} tudo certo, {Count(3)} sem diagnóstico";

    private IReadOnlyList<string> ExportColumns => VisibleColumns.Select(x => x.Label).ToList();

    private IEnumerable<IReadOnlyList<string?>> ExportRows() =>
        _filtered.Select(row => (IReadOnlyList<string?>)VisibleColumns.Select(c => (string?)CellText(row, c.Key)).ToList());

    private string BuildHtml() => ReportBuilder.TableHtml("Diagnóstico dos computadores", ExportSubtitle, ExportColumns, ExportRows());

    private byte[] BuildCsv() => ReportBuilder.Csv(ExportColumns, ExportRows());
}
