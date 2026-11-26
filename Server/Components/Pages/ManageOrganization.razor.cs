using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Remotely.Server.Components.ModalContents;
using Remotely.Server.Enums;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.ViewModels;
using System.Text;
using System.Text.Encodings.Web;

namespace Remotely.Server.Components.Pages;

public partial class ManageOrganization : AuthComponentBase
{
    private readonly List<DeviceGroup> _deviceGroups = new();
    private readonly List<InviteLink> _invites = new();
    private readonly List<RemotelyUser> _orgUsers = new();
    private bool _inviteAsAdmin;
    private string _inviteEmail = string.Empty;
    private bool _isLoading = true;
    private string _newDeviceGroupName = string.Empty;
    private Organization? _organization;
    private string _selectedDeviceGroupId = string.Empty;
    private readonly List<AccessProfile> _profiles = new();
    private AccessProfile? _editingProfile;
    private bool _isNewProfile;

    [Inject]
    private IDataService DataService { get; set; } = null!;

    [Inject]
    private IEmailSenderEx EmailSender { get; set; } = null!;

    [Inject]
    private IJsInterop JsInterop { get; set; } = null!;

    [Inject]
    private IModalService ModalService { get; set; } = null!;

    [Inject]
    private NavigationManager NavManager { get; set; } = null!;

    [Inject]
    private IToastService ToastService { get; set; } = null!;
    [Inject]
    private UserManager<RemotelyUser> UserManager { get; set; } = null!;


    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        await RefreshData();

        _isLoading = false;
    }

    private async Task CreateNewDeviceGroup()
    {
        EnsureUserSet();

        if (!User.IsAdministrator)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_newDeviceGroupName))
        {
            return;
        }

        var deviceGroup = new DeviceGroup()
        {
            Name = _newDeviceGroupName
        };

        var result = await DataService.AddDeviceGroup(User.OrganizationID, deviceGroup);
        if (!result.IsSuccess)
        {
            ToastService.ShowToast(result.Reason, classString: "bg-danger");
            return;
        }

        ToastService.ShowToast("Grupo de dispositivos criado.");
        _deviceGroups.Add(deviceGroup);
        _newDeviceGroupName = string.Empty;
    }

    private void DefaultOrgCheckChanged(ChangeEventArgs args)
    {
        if (_organization is null)
        {
            return;
        }

        EnsureUserSet();

        if (!User.IsServerAdmin)
        {
            return;
        }

        if (args.Value is not bool isDefault)
        {
            return;
        }

        DataService.SetIsDefaultOrganization(_organization.ID, isDefault);
        ToastService.ShowToast("Organização padrão definida.");
    }

    private async Task DeleteInvite(InviteLink invite)
    {
        EnsureUserSet();

        if (!User.IsAdministrator)
        {
            return;
        }

        var result = await JsInterop.Confirm(new ConfirmOptions("Excluir convite", "O link do convite deixa de funcionar.", "Excluir", Danger: true, Icon: "fa-solid fa-trash-can"));
        if (!result)
        {
            return;
        }

        await DataService.DeleteInvite(User.OrganizationID, invite.ID);
        _invites.RemoveAll(x => x.ID == invite.ID);
        ToastService.ShowToast("Convite excluído.");
    }

    private async Task UpdateGroupRemote(DeviceGroup group, string? consent, bool notify)
    {
        EnsureUserSet();
        var result = await DataService.UpdateDeviceGroupRemoteSettings(User.OrganizationID, group.ID, consent, notify);
        if (!result.IsSuccess)
        {
            ToastService.ShowToast2(result.Reason, ToastType.Warning);
            return;
        }

        group.RemoteControlConsent = string.IsNullOrWhiteSpace(consent) ? null : consent;
        group.NotifyRemoteSessions = notify;
        ToastService.ShowToast2($"Grupo {group.Name}: aceite \"{RemoteControlPolicy.Describe(group.RemoteControlConsent).ToLowerInvariant()}\"{(notify ? ", com aviso de sessões" : "")}.", ToastType.Success);
    }

    private async Task DeleteDeviceGroup(string deviceGroupId)
    {
        _selectedDeviceGroupId = deviceGroupId;
        await DeleteSelectedDeviceGroup();
    }

    private async Task DeleteSelectedDeviceGroup()
    {
        EnsureUserSet();

        if (!User.IsAdministrator)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedDeviceGroupId))
        {
            return;
        }

        var result = await JsInterop.Confirm(new ConfirmOptions("Excluir grupo", "Os dispositivos do grupo ficam sem grupo e os perfis de acesso deixam de considerá-lo.", "Excluir", Danger: true, Icon: "fa-solid fa-trash-can"));
        if (!result)
        {
            return;
        }

        await DataService.DeleteDeviceGroup(User.OrganizationID, _selectedDeviceGroupId);
        _deviceGroups.RemoveAll(x => x.ID == _selectedDeviceGroupId);
        _selectedDeviceGroupId = string.Empty;
    }

    private async Task DeleteUser(RemotelyUser user)
    {
        EnsureUserSet();

        if (!User.IsAdministrator)
        {
            return;
        }

        if (User.Id == user.Id)
        {
            ToastService.ShowToast("Você não pode excluir a si mesmo.", classString: "bg-warning");
            return;
        }

        var result = await JsInterop.Confirm(new ConfirmOptions("Excluir usuário", "O usuário perde o acesso ao painel imediatamente. Esta ação não pode ser desfeita.", "Excluir", Danger: true, Icon: "fa-solid fa-user-xmark"));
        if (!result)
        {
            return;
        }

        await DataService.DeleteUser(User.OrganizationID, user.Id);
        _orgUsers.RemoveAll(x => x.Id == user.Id);
        ToastService.ShowToast("Usuário excluído.");
    }

    private async Task EditDeviceGroups(RemotelyUser user)
    {
        void editDeviceGroupsModal(RenderTreeBuilder builder)
        {
            var deviceGroups = DataService.GetDeviceGroupsForOrganization(user.OrganizationID);

            builder.OpenComponent<EditDeviceGroup>(0);
            builder.AddAttribute(1, EditDeviceGroup.EditUserPropName, user);
            builder.AddAttribute(2, EditDeviceGroup.DeviceGroupsPropName, deviceGroups);
            builder.CloseComponent();
        }
        await ModalService.ShowModal("Grupos de dispositivos", editDeviceGroupsModal);
    }

    private async Task EvaluateInviteInputKeypress(KeyboardEventArgs args)
    {
        if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            await SendInvite();
        }
    }

    private async Task EvaluateNewDeviceGroupKeyPress(KeyboardEventArgs args)
    {
        if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            await CreateNewDeviceGroup();
        }
    }
    private void OrganizationNameChanged(ChangeEventArgs args)
    {
        if (_organization is null)
        {
            return;
        }

        EnsureUserSet();

        if (!User.IsAdministrator)
        {
            return;
        }

        if (args.Value is not string newName)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        if (newName.Length > 25)
        {
            ToastService.ShowToast("Use no máximo 25 caracteres.",
                classString: "bg-warning");
            return;
        }

        DataService.UpdateOrganizationName(_organization.ID, newName);
        _organization.OrganizationName = newName;
        ToastService.ShowToast("Nome da organização alterado.");
    }

    private async Task RefreshData()
    {
        EnsureUserSet();

        var orgResult = await DataService.GetOrganizationByUserName(UserName);
        if (!orgResult.IsSuccess)
        {
            ToastService.ShowToast2(orgResult.Reason, Enums.ToastType.Warning);
            return;
        }

        _organization = orgResult.Value;
        _orgUsers.Clear();
        _invites.Clear();
        _deviceGroups.Clear();

        _invites.AddRange(DataService.GetAllInviteLinks(User.OrganizationID).OrderBy(x => x.InvitedUser));
        _deviceGroups.AddRange(DataService.GetDeviceGroups(UserName).OrderBy(x => x.Name));
        var orgUsers = await DataService.GetAllUsersInOrganization(User.OrganizationID);
        _orgUsers.AddRange(orgUsers.OrderBy(x => x.UserName));
        LoadProfiles();
    }

    private void LoadProfiles()
    {
        EnsureUserSet();
        _profiles.Clear();
        _profiles.AddRange(AccessProfiles.GetProfiles(User.OrganizationID));
    }

    private string? GetUserProfileId(RemotelyUser user)
    {
        return _profiles.FirstOrDefault(x => x.UserIds.Contains(user.Id))?.Id;
    }

    private string GetGroupName(string groupId)
    {
        return _deviceGroups.FirstOrDefault(x => x.ID == groupId)?.Name ?? "(grupo removido)";
    }

    private void NewProfile()
    {
        _isNewProfile = true;
        _editingProfile = new AccessProfile
        {
            Name = string.Empty,
            AllowRemoteControl = true,
            AllowScripts = false,
            AllowDeviceManagement = false,
            RestrictToGroups = true
        };
    }

    private void EditProfile(AccessProfile profile)
    {
        _isNewProfile = false;
        _editingProfile = profile.Clone();
    }

    private void CancelProfileEdit()
    {
        _editingProfile = null;
    }

    private void ToggleProfileGroup(string groupId, ChangeEventArgs args)
    {
        if (_editingProfile is null)
        {
            return;
        }
        _editingProfile.DeviceGroupIds.Remove(groupId);
        if (args.Value is true)
        {
            _editingProfile.DeviceGroupIds.Add(groupId);
        }
    }

    private void ToggleProfileUser(string userId, ChangeEventArgs args)
    {
        if (_editingProfile is null)
        {
            return;
        }
        _editingProfile.UserIds.Remove(userId);
        if (args.Value is true)
        {
            _editingProfile.UserIds.Add(userId);
        }
    }

    private async Task SaveProfile()
    {
        EnsureUserSet();
        if (_editingProfile is null || !User.IsAdministrator)
        {
            return;
        }

        if (_editingProfile.RestrictToGroups && _editingProfile.DeviceGroupIds.Count == 0)
        {
            ToastService.ShowToast2("Escolha pelo menos um grupo ou desligue \"Só os grupos escolhidos\".", Enums.ToastType.Warning);
            return;
        }

        var result = await AccessProfiles.SaveProfile(User.OrganizationID, _editingProfile);
        if (!result.IsSuccess)
        {
            ToastService.ShowToast2(result.Reason, Enums.ToastType.Error);
            return;
        }

        ToastService.ShowToast2(_isNewProfile ? "Perfil criado." : "Perfil salvo.", Enums.ToastType.Success);
        _editingProfile = null;
        await RefreshData();
    }

    private async Task DeleteProfile(AccessProfile profile)
    {
        EnsureUserSet();
        if (!User.IsAdministrator)
        {
            return;
        }

        var confirmed = await JsInterop.Confirm(new ConfirmOptions(
            "Excluir perfil de acesso",
            $"Excluir o perfil \"{profile.Name}\"? Os usuários dele voltam a ter acesso total aos grupos em que já estão.",
            "Excluir", Danger: true, Icon: "fa-solid fa-trash-can"));
        if (!confirmed)
        {
            return;
        }

        var result = await AccessProfiles.DeleteProfile(User.OrganizationID, profile.Id);
        if (!result.IsSuccess)
        {
            ToastService.ShowToast2(result.Reason, Enums.ToastType.Error);
            return;
        }

        if (_editingProfile?.Id == profile.Id)
        {
            _editingProfile = null;
        }
        ToastService.ShowToast2("Perfil excluído.", Enums.ToastType.Success);
        LoadProfiles();
    }

    private async Task ChangeUserProfile(RemotelyUser orgUser, ChangeEventArgs args)
    {
        EnsureUserSet();
        if (!User.IsAdministrator)
        {
            return;
        }

        var profileId = $"{args.Value}";
        var result = await AccessProfiles.AssignUser(
            User.OrganizationID,
            orgUser.Id,
            string.IsNullOrWhiteSpace(profileId) ? null : profileId);

        if (!result.IsSuccess)
        {
            ToastService.ShowToast2(result.Reason, Enums.ToastType.Error);
            return;
        }

        var profileName = _profiles.FirstOrDefault(x => x.Id == profileId)?.Name;
        ToastService.ShowToast2(
            profileName is null
                ? $"{orgUser.UserName} agora não tem perfil (acesso total aos seus grupos)."
                : $"{orgUser.UserName} agora usa o perfil {profileName}.",
            Enums.ToastType.Success);

        await RefreshData();
    }
    private async Task ResetPassword(RemotelyUser user)
    {
        EnsureUserSet();

        if (!User.IsAdministrator)
        {
            return;
        }

        var code = await UserManager.GeneratePasswordResetTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var resetUrl = $"{NavManager.BaseUri}Account/ResetPassword?code={code}";

        await ModalService.ShowModal("Redefinição de senha", builder =>
        {
            builder.AddMarkupContent(0, $@"<div class=""mb-3"">Password Reset URL:</div>
                    <input readonly value=""{resetUrl}"" class=""form-control"" /> 
                    <div class=""mt-3"">NOTE: Give this URL to the user.  They must be logged out completely for it to work.</div>");
        });
    }

    private async Task SendInvite()
    {
        EnsureUserSet();

        if (!User.IsAdministrator)
        {
            return;
        }

        if (!DataService.DoesUserExist(_inviteEmail))
        {
            var result = await DataService.CreateUser(_inviteEmail, _inviteAsAdmin, User.OrganizationID);
            if (result.IsSuccess)
            {
                var userResult = await DataService.GetUserByName(_inviteEmail);
                if (!userResult.IsSuccess)
                {
                    ToastService.ShowToast2(userResult.Reason, Enums.ToastType.Warning);
                    return;
                }

                var user = userResult.Value;

                await UserManager.ConfirmEmailAsync(user, await UserManager.GenerateEmailConfirmationTokenAsync(user));

                _orgUsers.Add(user);

                _inviteAsAdmin = false;
                _inviteEmail = string.Empty;
                ToastService.ShowToast("Conta de usuário criada.");
                return;
            }
            else
            {
                ToastService.ShowToast("Falha ao criar o usuário.", classString: "bg-danger");
                return;
            }
        }
        else
        {

            var invite = new InviteViewModel()
            {
                InvitedUser = _inviteEmail,
                IsAdmin = _inviteAsAdmin
            };
            var newInvite = await DataService.AddInvite(User.OrganizationID, invite);

            if (!newInvite.IsSuccess)
            {
                ToastService.ShowToast($"Falha ao criar o convite. {newInvite.Reason}", classString: "bg-danger");
                return;
            }

            var inviteURL = $"{NavManager.BaseUri}Invite/{newInvite.Value.ID}";
            var emailResult = await EmailSender.SendEmailAsync(invite.InvitedUser, "Invitation to Organization in Remotely",
                    $@"<img src='{NavManager.BaseUri}images/Remotely_Logo.png'/>
                            <br><br>
                            Hello!
                            <br><br>
                            You've been invited to join an organization in Remotely.
                            <br><br>
                            You can join the organization by <a href='{HtmlEncoder.Default.Encode(inviteURL)}'>clicking here</a>.",
                    User.OrganizationID);
            if (emailResult)
            {
                ToastService.ShowToast("Convite enviado.");

                _inviteAsAdmin = false;
                _inviteEmail = string.Empty;
                _invites.Add(newInvite.Value);
            }
            else
            {
                ToastService.ShowToast("Erro ao enviar o e-mail de convite.", classString: "bg-danger");
            }
        }
    }

    private async Task SetUserIsAdmin(ChangeEventArgs args, RemotelyUser orgUser)
    {
        EnsureUserSet();

        if (!User.IsAdministrator)
        {
            return;
        }

        if (args.Value is not bool isAdmin)
        {
            return;
        }

        await DataService.ChangeUserIsAdmin(User.OrganizationID, orgUser.Id, isAdmin);
        ToastService.ShowToast("Permissão de administrador atualizada.");
    }

    private void ShowDefaultOrgHelp()
    {
        ModalService.ShowModal("Organização padrão", new[]
        {
            "Opção disponível só para administradores do servidor. Quando marcada, define esta " +
            "organização como padrão do servidor. Se os apps de suporte rápido não conseguirem " +
            "identificar a organização, usarão a personalização da organização padrão."
        });
    }

    private void ShowDeviceGroupHelp()
    {
        ModalService.ShowModal("Grupos de dispositivos", new[]
       {
            "Grupos de dispositivos servem para restringir permissões de usuários e filtrar " +
            "computadores na página inicial.",
            "Todos têm acesso aos dispositivos que não estão em um grupo. Só administradores e " +
            "usuários de um grupo têm acesso aos dispositivos daquele grupo."
        });
    }

    private void ShowInvitesHelp()
    {
        ModalService.ShowModal("Convites", new[]
       {
            "Os convites pendentes aparecem aqui e podem ser revogados excluindo-os.",

            "Se o usuário não existir, o convite cria a conta e já o adiciona à organização atual. " +
            "Um link de redefinição de senha pode ser gerado na tabela de usuários.",

            "A opção Admin define se o novo usuário terá privilégios de administrador nesta organização."
        });
    }

    private void ShowUsersHelp()
    {
        ModalService.ShowModal("Usuários", new[]
        {
            "Todos os usuários da organização são gerenciados aqui.",
            "Administradores têm acesso a esta tela e a todos os computadores."
        });
    }
}
