using Microsoft.EntityFrameworkCore;
using Remotely.Server.Data;
using Remotely.Server.Models;
using Remotely.Shared.Entities;
using Remotely.Shared.Primitives;
using System.Text.Json;

namespace Remotely.Server.Services;

public interface IAccessProfileService
{
    Task<Result> AssignUser(string organizationId, string userId, string? profileId);
    Task<Result> DeleteProfile(string organizationId, string profileId);
    UserPermissions GetPermissions(RemotelyUser? user);
    AccessProfile? GetProfileForUser(RemotelyUser user);
    IReadOnlyList<AccessProfile> GetProfiles(string organizationId);
    Task<Result<AccessProfile>> SaveProfile(string organizationId, AccessProfile profile);
}

/// <summary>
/// Guarda os perfis de acesso na tabela KeyValueRecords (um registro com todos os perfis,
/// por organização), sem exigir migração de banco. Os grupos visíveis de um perfil são
/// aplicados como associação dos usuários aos grupos de dispositivos, que é o que o resto
/// do sistema (painel, hubs e API) já usa para filtrar dispositivos.
/// </summary>
public class AccessProfileService : IAccessProfileService
{
    // Chave fixa do registro em KeyValueRecords.
    public static readonly Guid DbKey = Guid.Parse("6f0d3f6e-6a52-4c1f-9b0e-3a1c2d7e5b41");

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAppDbFactory _appDbFactory;
    private readonly ILogger<AccessProfileService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _cacheLock = new();
    private Dictionary<string, List<AccessProfile>>? _cache;

    public AccessProfileService(IAppDbFactory appDbFactory, ILogger<AccessProfileService> logger)
    {
        _appDbFactory = appDbFactory;
        _logger = logger;
    }

    public IReadOnlyList<AccessProfile> GetProfiles(string organizationId)
    {
        var all = Load();
        return all.TryGetValue(organizationId, out var profiles)
            ? profiles.Select(x => x.Clone()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<AccessProfile>();
    }

    public AccessProfile? GetProfileForUser(RemotelyUser user)
    {
        var all = Load();
        if (!all.TryGetValue(user.OrganizationID, out var profiles))
        {
            return null;
        }
        return profiles.FirstOrDefault(x => x.UserIds.Contains(user.Id))?.Clone();
    }

    public UserPermissions GetPermissions(RemotelyUser? user)
    {
        if (user is null)
        {
            return new UserPermissions(false, false, false, false, null);
        }

        if (user.IsAdministrator)
        {
            return UserPermissions.Full;
        }

        var profile = GetProfileForUser(user);
        if (profile is null)
        {
            // Sem perfil: mesmo comportamento de antes dos perfis existirem.
            return UserPermissions.Full;
        }

        return new UserPermissions(
            profile.AllowRemoteControl,
            profile.AllowScripts,
            profile.AllowDeviceManagement,
            profile.AllowRepairs,
            profile.Name);
    }

    public async Task<Result<AccessProfile>> SaveProfile(string organizationId, AccessProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            return Result.Fail<AccessProfile>("Informe o nome do perfil.");
        }

        profile.Name = profile.Name.Trim();
        if (profile.Name.Length > 60)
        {
            return Result.Fail<AccessProfile>("O nome do perfil pode ter até 60 caracteres.");
        }

        await _writeLock.WaitAsync();
        try
        {
            var all = Load();
            var profiles = all.TryGetValue(organizationId, out var existing) ? existing : new List<AccessProfile>();

            if (profiles.Any(x => x.Id != profile.Id &&
                string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return Result.Fail<AccessProfile>("Já existe um perfil com esse nome.");
            }

            using var dbContext = _appDbFactory.GetContext();
            var validGroupIds = dbContext.DeviceGroups
                .Where(x => x.OrganizationID == organizationId)
                .Select(x => x.ID)
                .ToHashSet();
            var validUserIds = dbContext.Users
                .Where(x => x.OrganizationID == organizationId)
                .Select(x => x.Id)
                .ToHashSet();

            var toSave = profile.Clone();
            toSave.DeviceGroupIds = toSave.DeviceGroupIds.Distinct().Where(validGroupIds.Contains).ToList();
            toSave.UserIds = toSave.UserIds.Distinct().Where(validUserIds.Contains).ToList();

            // Um usuário só pode estar em um perfil.
            foreach (var other in profiles.Where(x => x.Id != toSave.Id))
            {
                other.UserIds.RemoveAll(toSave.UserIds.Contains);
            }

            var index = profiles.FindIndex(x => x.Id == toSave.Id);
            if (index >= 0)
            {
                profiles[index] = toSave;
            }
            else
            {
                profiles.Add(toSave);
            }

            all[organizationId] = profiles;
            await Persist(dbContext, all);

            if (toSave.RestrictToGroups)
            {
                await ApplyGroups(dbContext, organizationId, toSave.UserIds, toSave.DeviceGroupIds);
            }

            return Result.Ok(toSave.Clone());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while saving access profile.");
            return Result.Fail<AccessProfile>("Não foi possível salvar o perfil.");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<Result> DeleteProfile(string organizationId, string profileId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var all = Load();
            if (!all.TryGetValue(organizationId, out var profiles) ||
                profiles.RemoveAll(x => x.Id == profileId) == 0)
            {
                return Result.Fail("Perfil não encontrado.");
            }

            using var dbContext = _appDbFactory.GetContext();
            await Persist(dbContext, all);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting access profile.");
            return Result.Fail("Não foi possível excluir o perfil.");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<Result> AssignUser(string organizationId, string userId, string? profileId)
    {
        await _writeLock.WaitAsync();
        try
        {
            using var dbContext = _appDbFactory.GetContext();
            var userExists = dbContext.Users.Any(x => x.Id == userId && x.OrganizationID == organizationId);
            if (!userExists)
            {
                return Result.Fail("Usuário não encontrado.");
            }

            var all = Load();
            var profiles = all.TryGetValue(organizationId, out var existing) ? existing : new List<AccessProfile>();

            AccessProfile? target = null;
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                target = profiles.FirstOrDefault(x => x.Id == profileId);
                if (target is null)
                {
                    return Result.Fail("Perfil não encontrado.");
                }
            }

            foreach (var profile in profiles)
            {
                profile.UserIds.Remove(userId);
            }
            target?.UserIds.Add(userId);

            all[organizationId] = profiles;
            await Persist(dbContext, all);

            if (target?.RestrictToGroups == true)
            {
                await ApplyGroups(dbContext, organizationId, new[] { userId }, target.DeviceGroupIds);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while assigning access profile.");
            return Result.Fail("Não foi possível alterar o perfil do usuário.");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task ApplyGroups(
        AppDb dbContext,
        string organizationId,
        IEnumerable<string> userIds,
        IEnumerable<string> groupIds)
    {
        var userIdSet = userIds.ToHashSet();
        var groupIdSet = groupIds.ToHashSet();
        if (userIdSet.Count == 0)
        {
            return;
        }

        var users = await dbContext.Users
            .Include(x => x.DeviceGroups)
            .Where(x => x.OrganizationID == organizationId && userIdSet.Contains(x.Id))
            .ToListAsync();

        var groups = await dbContext.DeviceGroups
            .Where(x => x.OrganizationID == organizationId && groupIdSet.Contains(x.ID))
            .ToListAsync();

        foreach (var user in users)
        {
            // Administradores enxergam tudo de qualquer forma; não mexe nos grupos deles.
            if (user.IsAdministrator)
            {
                continue;
            }

            user.DeviceGroups ??= new List<DeviceGroup>();
            user.DeviceGroups.Clear();
            foreach (var group in groups)
            {
                user.DeviceGroups.Add(group);
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private Dictionary<string, List<AccessProfile>> Load()
    {
        lock (_cacheLock)
        {
            if (_cache is not null)
            {
                return Copy(_cache);
            }
        }

        Dictionary<string, List<AccessProfile>> loaded;
        try
        {
            using var dbContext = _appDbFactory.GetContext();
            var record = dbContext.KeyValueRecords.AsNoTracking().FirstOrDefault(x => x.Key == DbKey);
            loaded = string.IsNullOrWhiteSpace(record?.Value)
                ? new()
                : JsonSerializer.Deserialize<Dictionary<string, List<AccessProfile>>>(record.Value, _jsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading access profiles.");
            loaded = new();
        }

        lock (_cacheLock)
        {
            _cache = loaded;
            return Copy(_cache);
        }
    }

    private async Task Persist(AppDb dbContext, Dictionary<string, List<AccessProfile>> all)
    {
        var json = JsonSerializer.Serialize(all, _jsonOptions);
        var record = await dbContext.KeyValueRecords.FirstOrDefaultAsync(x => x.Key == DbKey);
        if (record is null)
        {
            await dbContext.KeyValueRecords.AddAsync(new KeyValueRecord { Key = DbKey, Value = json });
        }
        else
        {
            record.Value = json;
        }
        await dbContext.SaveChangesAsync();

        lock (_cacheLock)
        {
            _cache = Copy(all);
        }
    }

    private static Dictionary<string, List<AccessProfile>> Copy(Dictionary<string, List<AccessProfile>> source)
    {
        return source.ToDictionary(
            x => x.Key,
            x => x.Value.Select(p => p.Clone()).ToList());
    }
}
