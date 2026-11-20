using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Remotely.Server.Services;
using Remotely.Shared.Entities;

namespace Remotely.Server.Components.Pages;

[Authorize]
public partial class ScriptsPage : AuthComponentBase
{
    private List<SavedScript> _allScripts = new();
    private bool _showOnlyMyScripts;
    private int _libraryKey;

    [Parameter]
    public string? ActiveTab { get; set; }

    [SupplyParameterFromQuery(Name = "novo")]
    public string? NewScript { get; set; }

    [Inject]
    private IDataService DataService { get; init; } = null!;

    [Inject]
    private NavigationManager NavManager { get; init; } = null!;

    public string CurrentTab => ActiveTab ?? "saved-scripts";

    /// <summary>
    /// Script escolhido em outra aba (ex.: "Executar" na biblioteca) para já vir selecionado.
    /// </summary>
    public Guid? PendingScriptId { get; set; }

    /// <summary>
    /// Pedido de "novo script" vindo do botão do cabeçalho.
    /// </summary>
    public bool StartWithNewScript => NewScript == "1";

    public bool ShowOnlyMyScripts
    {
        get => _showOnlyMyScripts;
        set => _showOnlyMyScripts = value;
    }

    public IReadOnlyList<SavedScript> AllScripts => _allScripts;

    public IEnumerable<SavedScript> VisibleScripts
    {
        get
        {
            if (User is null)
            {
                return Enumerable.Empty<SavedScript>();
            }

            return _allScripts.Where(x =>
                x.CreatorId == User.Id ||
                (!ShowOnlyMyScripts && x.IsPublic));
        }
    }

    public IEnumerable<string> KnownFolders => _allScripts
        .Select(x => x.FolderPath)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x!.Trim('/'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x);

    public void OpenInRun(Guid scriptId)
    {
        PendingScriptId = scriptId;
        NavManager.NavigateTo("/scripts/run-script");
    }

    public void OpenInSchedules(Guid scriptId)
    {
        PendingScriptId = scriptId;
        NavManager.NavigateTo("/scripts/script-schedules");
    }

    public Guid? TakePendingScriptId()
    {
        var id = PendingScriptId;
        PendingScriptId = null;
        return id;
    }

    public async Task RefreshScripts()
    {
        EnsureUserSet();
        _allScripts = await DataService.GetSavedScriptsWithoutContent(User.Id, User.OrganizationID);
        await InvokeAsync(StateHasChanged);
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await RefreshScripts();
    }

    protected override void OnParametersSet()
    {
        if (StartWithNewScript)
        {
            // Força a biblioteca a abrir limpa, com um script novo.
            _libraryKey++;
        }
        base.OnParametersSet();
    }

    private string TabClass(string tab) => CurrentTab == tab ? "active" : string.Empty;
}
