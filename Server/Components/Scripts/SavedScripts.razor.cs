using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Remotely.Server.Components.Pages;
using Remotely.Server.Enums;
using Remotely.Server.Services;
using Remotely.Shared.Entities;

namespace Remotely.Server.Components.Scripts;

[Authorize]
public partial class SavedScripts : AuthComponentBase
{
    [CascadingParameter]
    private ScriptsPage ParentPage { get; set; } = null!;

    private SavedScript _selectedScript = new() { Name = string.Empty };
    private string _alertMessage = string.Empty;
    private string _alertOptionsShowClass = string.Empty;
    private string _environmentVarsShowClass = string.Empty;

    [Inject]
    public IDataService DataService { get; set; } = null!;

    [Inject]
    public IToastService ToastService { get; set; } = null!;

    [Inject]
    public IJsInterop JsInterop { get; set; } = null!;

    [Inject]
    public IModalService ModalService { get; set; } = null!;

    [Inject]
    public required ILogger<SavedScripts> Logger { get; set; }

    private bool CanModifyScript
    {
        get
        {
            if (User is null)
            {
                return false;
            }

            return 
                _selectedScript.Id == Guid.Empty ||
                _selectedScript.CreatorId == User.Id || User.IsAdministrator;
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

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            JsInterop.AutoHeight();
        }
        base.OnAfterRender(firstRender);
    }

    private async Task OnValidSubmit(EditContext context)
    {
        EnsureUserSet();

        if (_selectedScript is null)
        {
            return;
        }

        if (!CanModifyScript)
        {
            ToastService.ShowToast("Você não pode alterar scripts de outras pessoas.", classString: "bg-warning");
            return;
        }
        
        await DataService.AddOrUpdateSavedScript(_selectedScript, User.Id);
        await ParentPage.RefreshScripts();
        ToastService.ShowToast("Script salvo.");
        _alertMessage = "Script salvo.";
    }

    private void CreateNew()
    {
        _selectedScript = new()
        {
            Name = string.Empty
        };
    }

    private async Task DeleteSelectedScript()
    {
        try
        {
            if (!CanDeleteScript)
            {
                ToastService.ShowToast("Você não pode excluir scripts de outras pessoas.", classString: "bg-warning");
                return;
            }

            var result = await JsInterop.Confirm($"Tem certeza de que quer excluir o script {_selectedScript.Name}?");
            if (result)
            {
                await DataService.DeleteSavedScript(_selectedScript.Id);
                ToastService.ShowToast("Script excluído.");
                _alertMessage = "Script excluído.";
                await ParentPage.RefreshScripts();
                _selectedScript = new()
                {
                    Name = string.Empty
                };
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while deleting script.");
            ToastService.ShowToast2("Falha ao excluir o script", ToastType.Error);
        }

    }

    private async Task ScriptSelected(ScriptTreeNode viewModel)
    {
        EnsureUserSet();
        if (viewModel.Script is not null)
        {
            var result = await DataService.GetSavedScript(User.Id, viewModel.Script.Id);
            if (result.IsSuccess)
            {
                _selectedScript = result.Value;
                return;
            }
        }

        _selectedScript = new()
        {
            Name = string.Empty
        };
    }

    private void ToggleEnvironmentVarsShown()
    {
        if (string.IsNullOrWhiteSpace(_environmentVarsShowClass))
        {
            _environmentVarsShowClass = "show";
        }
        else
        {
            _environmentVarsShowClass = string.Empty;
        }
    }
    private void ToggleAlertOptionsShown()
    {
        if (string.IsNullOrWhiteSpace(_alertOptionsShowClass))
        {
            _alertOptionsShowClass = "show";
        }
        else
        {
            _alertOptionsShowClass = string.Empty;
        }
    }

    private void ShowAlertErrorHelp()
    {
        ModalService.ShowModal("Alertas de erro de script", new[]
        {
            "Um script é considerado com erro quando escreve algo na saída de erro. " +
            "Em CMD, WinPS e Bash, um código de saída diferente de 0 também conta como erro. " +
            "(O PowerShell Core integrado não usa códigos de saída.)",

            "No PowerShell Core, qualquer texto nas saídas Warning ou Error gera um alerta. " +
            "Para gerar um erro conforme alguma lógica do script, use " +
            "'Write-Error' ou 'Write-Warning' em qualquer ponto do script.",

            "No Windows PowerShell, use 'Write-Error' para gerar um alerta.",

            "No Bash e no CMD, saia com qualquer código diferente de zero."
        });
    }
}
