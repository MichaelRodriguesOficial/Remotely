using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Remotely.Server.Components.Pages;
using Remotely.Server.Enums;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Enums;

namespace Remotely.Server.Components.Scripts;

[Authorize]
public partial class SavedScripts : AuthComponentBase
{
    private SavedScript _selectedScript = NewScript();

    [CascadingParameter]
    private ScriptsPage ParentPage { get; set; } = null!;

    [Inject]
    public IDataService DataService { get; set; } = null!;

    [Inject]
    public IToastService ToastService { get; set; } = null!;

    [Inject]
    public IJsInterop JsInterop { get; set; } = null!;

    [Inject]
    public required ILogger<SavedScripts> Logger { get; set; }

    private bool IsNew => _selectedScript.Id == Guid.Empty;

    private int LineCount => string.IsNullOrEmpty(_selectedScript.Content)
        ? 0
        : _selectedScript.Content.Split('\n').Length;

    private bool CanModifyScript
    {
        get
        {
            if (User is null)
            {
                return false;
            }

            return
                IsNew ||
                _selectedScript.CreatorId == User.Id ||
                User.IsAdministrator;
        }
    }

    private bool CanDeleteScript
    {
        get
        {
            if (User is null)
            {
                return false;
            }

            return
                !string.IsNullOrWhiteSpace(_selectedScript.CreatorId) &&
                (_selectedScript.CreatorId == User.Id || User.IsAdministrator);
        }
    }

    private static SavedScript NewScript() => new()
    {
        Name = string.Empty,
        Shell = ScriptingShell.PSCore
    };

    private string GetPlaceholder()
    {
        return _selectedScript.Shell switch
        {
            ScriptingShell.Bash => "#!/bin/bash\ndf -h",
            ScriptingShell.CMD => "ipconfig /all",
            _ => "Get-ComputerInfo | Select-Object CsName, OsName, OsVersion"
        };
    }

    private async Task OnValidSubmit(EditContext context)
    {
        EnsureUserSet();

        if (!CanModifyScript)
        {
            ToastService.ShowToast2("Você não pode alterar scripts de outras pessoas.", ToastType.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedScript.Content))
        {
            ToastService.ShowToast2("O script está vazio.", ToastType.Warning);
            return;
        }

        _selectedScript.FolderPath = _selectedScript.FolderPath?.Trim().Trim('/');

        try
        {
            var result = await DataService.AddOrUpdateSavedScript(_selectedScript, User.Id);
            if (!result.IsSuccess)
            {
                ToastService.ShowToast2($"Falha ao salvar: {result.Reason}", ToastType.Error);
                return;
            }

            await ParentPage.RefreshScripts();
            ToastService.ShowToast2("Script salvo.", ToastType.Success);

            // O EF preenche o Id de scripts novos ao salvar; recarrega com o criador.
            await LoadScript(_selectedScript.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while saving script.");
            ToastService.ShowToast2("Falha ao salvar o script.", ToastType.Error);
        }
    }

    private void CreateNew()
    {
        _selectedScript = NewScript();
    }

    private void Duplicate()
    {
        _selectedScript = new SavedScript()
        {
            Name = $"{_selectedScript.Name} (cópia)",
            Content = _selectedScript.Content,
            FolderPath = _selectedScript.FolderPath,
            Shell = _selectedScript.Shell,
            IsPublic = _selectedScript.IsPublic,
            IsQuickScript = _selectedScript.IsQuickScript,
            GenerateAlertOnError = _selectedScript.GenerateAlertOnError,
            SendEmailOnError = _selectedScript.SendEmailOnError,
            SendErrorEmailTo = _selectedScript.SendErrorEmailTo
        };
        ToastService.ShowToast2("Cópia criada. Ajuste e salve.", ToastType.Info);
    }

    private async Task DeleteSelectedScript()
    {
        try
        {
            if (!CanDeleteScript)
            {
                ToastService.ShowToast2("Você não pode excluir scripts de outras pessoas.", ToastType.Warning);
                return;
            }

            var result = await JsInterop.Confirm($"Excluir o script \"{_selectedScript.Name}\"? Agendamentos que usam este script deixarão de funcionar.");
            if (!result)
            {
                return;
            }

            await DataService.DeleteSavedScript(_selectedScript.Id);
            ToastService.ShowToast2("Script excluído.", ToastType.Success);
            await ParentPage.RefreshScripts();
            _selectedScript = NewScript();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while deleting script.");
            ToastService.ShowToast2("Falha ao excluir o script.", ToastType.Error);
        }
    }

    private async Task ScriptSelected(SavedScript script)
    {
        await LoadScript(script.Id);
    }

    private async Task LoadScript(Guid scriptId)
    {
        EnsureUserSet();
        var result = await DataService.GetSavedScript(User.Id, scriptId);
        _selectedScript = result.IsSuccess ? result.Value : NewScript();
    }
}
